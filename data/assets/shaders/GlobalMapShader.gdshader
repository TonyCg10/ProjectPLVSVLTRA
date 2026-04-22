shader_type spatial;
render_mode unshaded;

// Datos base
uniform sampler2D id_map : filter_nearest, hint_default_black;
uniform sampler2D country_lookup : filter_nearest;
uniform sampler2D state_lookup : filter_nearest;
uniform sampler2D height_map : filter_linear;

// Datos visuales
uniform sampler2D albedo_tex : source_color; 

// Configuración
uniform int map_mode : hint_range(0, 3) = 3; 
uniform float height_scale : hint_range(0.0, 5.0) = 1.5;

// Ajustes de imagen
uniform float brightness : hint_range(0.0, 2.0) = 1.0;
uniform float contrast : hint_range(0.5, 2.0) = 1.1;
uniform float saturation : hint_range(0.0, 2.0) = 1.1;

// Detalle Papel
uniform float grain_strength : hint_range(0.0, 1.0) = 0.2;

// Selección
uniform int selected_country_idx = -1;
uniform int selected_state_idx = -1;
uniform vec3 selection_color = vec3(1.0, 1.0, 1.0);

// =========================================
// SISTEMA DE RUIDO SUAVE (RESTAURADO)
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
    vec4 id_sample = texture(id_map, UV);
    int node_id = int(round(id_sample.r * 255.0)) + 
                  (int(round(id_sample.g * 255.0)) * 256) + 
                  (int(round(id_sample.b * 255.0)) * 65536);
    
    int array_idx = node_id - 1;
    ivec2 data_coords = ivec2(array_idx % 4096, array_idx / 4096);
    
    // 1. Color Natural + Grano de Papel (Suave)
    vec3 col_nat = texture(albedo_tex, UV).rgb;
    float grain = fbm(UV * 1000.0);
    col_nat *= (0.9 + grain * grain_strength);
    
    col_nat *= brightness;
    col_nat = (col_nat - 0.5) * contrast + 0.5;
    float grayscale = dot(col_nat, vec3(0.299, 0.587, 0.114));
    col_nat = mix(vec3(grayscale), col_nat, saturation);

    // 2. Color Político
    vec3 col_pol;
    if (map_mode == 1) col_pol = texelFetch(country_lookup, data_coords, 0).rgb;
    else if (map_mode == 2) col_pol = texelFetch(state_lookup, data_coords, 0).rgb;
    else if (map_mode == 0) col_pol = vec3(texture(height_map, UV).r);
    else col_pol = col_nat;

    // 3. Mezcla Final
    float cam_h = INV_VIEW_MATRIX[3].y;
    float alpha = mix(0.12, 0.45, smoothstep(30.0, 100.0, cam_h));
    vec3 final_col = (map_mode == 3) ? col_nat : mix(col_nat, col_pol, alpha);

    // 4. Bordes Políticos
    vec2 ps = 1.0 / vec2(textureSize(id_map, 0));
    vec4 n1 = texture(id_map, UV + vec2(ps.x, 0.0) * 1.5);
    if (distance(id_sample, n1) > 0.01 && (map_mode == 1 || map_mode == 2)) {
        final_col = mix(final_col, vec3(1.0), 0.3);
    }

    ALBEDO = final_col;
}
