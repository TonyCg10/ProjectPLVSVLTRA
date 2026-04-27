shader_type spatial;
render_mode unshaded, depth_draw_opaque;

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
uniform float height_scale : hint_range(0.0, 5.0) = 0.2;

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
uniform float political_edge_aa : hint_range(0.0, 2.0) = 1.0;

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

int decode_node_id(vec3 enc) {
    return int(round(enc.r * 255.0)) +
           (int(round(enc.g * 255.0)) * 256) +
           (int(round(enc.b * 255.0)) * 65536);
}

ivec2 node_coords(int node_id) {
    int idx = max(node_id - 1, 0);
    return ivec2(idx % 4096, idx / 4096);
}

void vertex() {
    // El mapa global de estrategia siempre es plano para evitar picos poligonales
    VERTEX.y += 0.0;
}

void fragment() {
    // 1. Sample node identity
    vec4 id_sample = texture(id_map, UV);
    int node_id = decode_node_id(id_sample.rgb);

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

    // 3. Political color with 4-tap subpixel AA
    vec2 ps = 1.0 / vec2(textureSize(id_map, 0));
    vec2 aa_off = ps * (0.5 * political_edge_aa);
    vec2 uv0 = UV + vec2(-aa_off.x, -aa_off.y);
    vec2 uv1 = UV + vec2( aa_off.x, -aa_off.y);
    vec2 uv2 = UV + vec2(-aa_off.x,  aa_off.y);
    vec2 uv3 = UV + vec2( aa_off.x,  aa_off.y);

    ivec2 c0 = node_coords(decode_node_id(texture(id_map, uv0).rgb));
    ivec2 c1 = node_coords(decode_node_id(texture(id_map, uv1).rgb));
    ivec2 c2 = node_coords(decode_node_id(texture(id_map, uv2).rgb));
    ivec2 c3 = node_coords(decode_node_id(texture(id_map, uv3).rgb));

    vec3 cc0 = texelFetch(country_lookup, c0, 0).rgb;
    vec3 cc1 = texelFetch(country_lookup, c1, 0).rgb;
    vec3 cc2 = texelFetch(country_lookup, c2, 0).rgb;
    vec3 cc3 = texelFetch(country_lookup, c3, 0).rgb;
    vec3 col_country = (cc0 + cc1 + cc2 + cc3) * 0.25;

    vec3 sc0 = texelFetch(state_lookup, c0, 0).rgb;
    vec3 sc1 = texelFetch(state_lookup, c1, 0).rgb;
    vec3 sc2 = texelFetch(state_lookup, c2, 0).rgb;
    vec3 sc3 = texelFetch(state_lookup, c3, 0).rgb;
    vec3 col_state = (sc0 + sc1 + sc2 + sc3) * 0.25;

    float c_var = max(max(distance(cc0, cc1), distance(cc0, cc2)), distance(cc0, cc3));
    float s_var = max(max(distance(sc0, sc1), distance(sc0, sc2)), distance(sc0, sc3));
    float country_border = smoothstep(0.03, 0.12, c_var);
    float state_border = smoothstep(0.03, 0.12, s_var);

    // 5. Final color composition
    vec3 final_col;
    if (map_mode == 1) {
        // Country mode: solid political color blended with natural
        final_col = mix(col_nat, col_country, 0.55);
        final_col = mix(final_col, border_color_country, country_border);
    } else if (map_mode == 2) {
        // State mode: solid political color blended with natural
        final_col = mix(col_nat, col_state, 0.55);
        final_col = mix(final_col, border_color_state, state_border);
        final_col = mix(final_col, border_color_country, country_border);
    } else if (map_mode == 0) {
        // Height/node debug
        final_col = vec3(texture(height_map, UV).r);
    } else {
        // Natural mode (3) — show country borders subtly
        final_col = col_nat;
        final_col = mix(final_col, border_color_country, country_border * 0.3);
    }

    // 6. Clone fade y transparencia del océano
    float dist_to_cam = distance(NODE_POSITION_WORLD.xz, CAMERA_POSITION_WORLD.xz);
    float fade_out = smoothstep(350.0, 550.0, dist_to_cam);
    ALBEDO = mix(final_col, vec3(0.0), portal_fade);
    ALBEDO = mix(ALBEDO, vec3(0.0), fade_out);
    
    // Si la altura es prácticamente 0, es océano: descartar para ver el WaterShader debajo
    // y evitar bugs de profundidad/transparencia.
    float h = texture(height_map, UV).r;
    if (h < 0.005) {
        discard;
    }
}
