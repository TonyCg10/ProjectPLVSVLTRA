shader_type spatial;
render_mode unshaded;

// Base data
uniform sampler2D id_map : filter_nearest, hint_default_black;
uniform sampler2D country_lookup : filter_nearest;
uniform sampler2D state_lookup : filter_nearest;
uniform sampler2D height_map : filter_linear;

// Visual data
uniform sampler2D albedo_tex : source_color;
uniform sampler2DArray world_data_tex;

// Configuration
uniform int map_mode : hint_range(0, 3) = 3;  // 0=Nodes, 1=Countries, 2=States, 3=Natural
uniform float height_scale : hint_range(0.0, 5.0) = 1.5;

// Image adjustments
uniform float brightness : hint_range(0.0, 2.0) = 1.0;
uniform float contrast : hint_range(0.5, 2.0) = 1.1;
uniform float saturation : hint_range(0.0, 2.0) = 1.1;

// Paper grain
uniform float grain_strength : hint_range(0.0, 1.0) = 0.2;

// Selection
uniform int selected_country_idx = -1;
uniform int selected_state_idx = -1;
uniform vec3 selection_color = vec3(1.0, 1.0, 1.0);

// Transition
uniform float portal_fade : hint_range(0.0, 1.0) = 0.0;

// Border style
uniform float border_width : hint_range(1.0, 5.0) = 1.5;
uniform vec3 border_color_country : source_color = vec3(0.08, 0.08, 0.06);
uniform vec3 border_color_state : source_color = vec3(0.15, 0.12, 0.10);

// =========================================
// NOISE SYSTEM
// =========================================
float hash(vec2 p) {
    p = fract(p * vec2(123.34, 456.21));
    p += dot(p, p + 45.32);
    return fract(p.x * p.y);
}

float noise(vec2 x) {
    vec2 p = floor(x);
    vec2 f = fract(x);
    f = f * f * (3.0 - 2.0 * f);
    return mix(mix(hash(p), hash(p + vec2(1.0, 0.0)), f.x),
               mix(hash(p + vec2(0.0, 1.0)), hash(p + vec2(1.0, 1.0)), f.x), f.y);
}

float fbm(vec2 x) {
    float v = 0.0;
    float a = 0.5;
    mat2 rot = mat2(vec2(0.8, 0.6), vec2(-0.6, 0.8));
    for (int i = 0; i < 3; ++i) {
        v += a * noise(x);
        x = rot * x * 2.0 + vec2(10.0);
        a *= 0.5;
    }
    return v;
}

void vertex() {
    float h = texture(height_map, UV).r;
    VERTEX.y += h * height_scale;
}

void fragment() {
    // 1. Sample node identity
    vec4 id_sample = texture(id_map, UV);
    int node_id = int(round(id_sample.r * 255.0)) +
                  (int(round(id_sample.g * 255.0)) * 256) +
                  (int(round(id_sample.b * 255.0)) * 65536);

    int array_idx = node_id - 1;
    ivec2 data_coords = ivec2(array_idx % 4096, array_idx / 4096);

    // 2. Natural color + Paper grain
    vec3 col_nat = texture(albedo_tex, UV).rgb;
    float grain = fbm(UV * 1000.0);
    col_nat *= (0.9 + grain * grain_strength);

    col_nat *= brightness;
    col_nat = (col_nat - 0.5) * contrast + 0.5;
    float grayscale = dot(col_nat, vec3(0.299, 0.587, 0.114));
    col_nat = mix(vec3(grayscale), col_nat, saturation);

    // 3. Political color from lookup textures (each pixel = country/state color)
    vec3 col_country = texelFetch(country_lookup, data_coords, 0).rgb;
    vec3 col_state = texelFetch(state_lookup, data_coords, 0).rgb;

    // 4. Detect POLITICAL borders (country or state boundaries, not node boundaries)
    vec2 ps = 1.0 / vec2(textureSize(id_map, 0));
    float country_border = 0.0;
    float state_border = 0.0;

    // Sample 4 neighbors and compare their LOOKUP color (political identity)
    for (float d = 1.0; d <= border_width; d += 1.0) {
        // Right neighbor
        vec4 nb_r = texture(id_map, UV + vec2(ps.x * d, 0.0));
        int nid_r = int(round(nb_r.r * 255.0)) + (int(round(nb_r.g * 255.0)) * 256) + (int(round(nb_r.b * 255.0)) * 65536);
        ivec2 nc_r = ivec2((nid_r - 1) % 4096, (nid_r - 1) / 4096);

        // Up neighbor
        vec4 nb_u = texture(id_map, UV + vec2(0.0, ps.y * d));
        int nid_u = int(round(nb_u.r * 255.0)) + (int(round(nb_u.g * 255.0)) * 256) + (int(round(nb_u.b * 255.0)) * 65536);
        ivec2 nc_u = ivec2((nid_u - 1) % 4096, (nid_u - 1) / 4096);

        // Compare country colors
        vec3 nb_country_r = texelFetch(country_lookup, nc_r, 0).rgb;
        vec3 nb_country_u = texelFetch(country_lookup, nc_u, 0).rgb;
        if (distance(col_country, nb_country_r) > 0.02 || distance(col_country, nb_country_u) > 0.02) {
            country_border = 1.0;
        }

        // Compare state colors
        vec3 nb_state_r = texelFetch(state_lookup, nc_r, 0).rgb;
        vec3 nb_state_u = texelFetch(state_lookup, nc_u, 0).rgb;
        if (distance(col_state, nb_state_r) > 0.02 || distance(col_state, nb_state_u) > 0.02) {
            state_border = 1.0;
        }
    }

    // 5. Final color composition
    vec3 final_col;
    if (map_mode == 1) {
        // Country mode: solid political color blended with natural
        final_col = mix(col_nat, col_country, 0.55);
        if (country_border > 0.5) final_col = border_color_country;
    } else if (map_mode == 2) {
        // State mode: solid political color blended with natural
        final_col = mix(col_nat, col_state, 0.55);
        if (state_border > 0.5) final_col = border_color_state;
        // Also show country borders as thicker/darker lines
        if (country_border > 0.5) final_col = border_color_country;
    } else if (map_mode == 0) {
        // Height/node debug
        final_col = vec3(texture(height_map, UV).r);
    } else {
        // Natural mode (3) — show country borders subtly
        final_col = col_nat;
        if (country_border > 0.5) final_col = mix(final_col, border_color_country, 0.3);
    }

    // 6. Clone fade
    float dist_to_cam = distance(NODE_POSITION_WORLD.xz, CAMERA_POSITION_WORLD.xz);
    float fade_out = smoothstep(350.0, 550.0, dist_to_cam);
    ALBEDO = mix(final_col, vec3(0.0), portal_fade);
    ALBEDO = mix(ALBEDO, vec3(0.0), fade_out);
}
