shader_type spatial;

// Datos base
uniform sampler2D id_map : filter_nearest, hint_default_black;
uniform sampler2D country_lookup : filter_nearest;
uniform sampler2D state_lookup : filter_nearest;
uniform sampler2D height_map : filter_linear;

// Datos visuales
uniform sampler2D albedo_tex : source_color; 
uniform sampler2D normal_tex : hint_normal;  

// Configuración
uniform int map_mode : hint_range(0, 3) = 3; 
uniform float height_scale : hint_range(0.0, 50.0) = 15.0;

// Texturas 2K de Detalle (Splatting)
uniform sampler2D grass_albedo : source_color, hint_default_white;
uniform sampler2D grass_normal : hint_normal;
uniform sampler2D sand_albedo : source_color, hint_default_white;
uniform sampler2D sand_normal : hint_normal;
uniform sampler2D rock_albedo : source_color, hint_default_white;
uniform sampler2D rock_normal : hint_normal;

// Ajustes de imagen
uniform float brightness : hint_range(0.0, 2.0) = 1.0;
uniform float contrast : hint_range(0.5, 2.0) = 1.1;
uniform float saturation : hint_range(0.0, 2.0) = 1.1;

// Detalle Procedural (Tiling)
uniform float detail_strength : hint_range(0.0, 1.0) = 0.7;
uniform float detail_scale : hint_range(1.0, 3000.0) = 1500.0;

// Selección
uniform int selected_country_idx = -1;
uniform int selected_state_idx = -1;
uniform vec3 selection_color = vec3(1.0, 1.0, 1.0);

varying float v_height;
varying vec2 v_uv;

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
    for (int i = 0; i < 4; ++i) {
        v += a * noise(x);
        x = rot * x * 2.0 + vec2(10.0);
        a *= 0.5;
    }
    return v;
}

void vertex() {
    v_uv = UV;
    v_height = texture(height_map, UV).r;
    
    // Desplazamiento regional suave
    VERTEX.y += v_height * height_scale;
    
    // Normal simple hacia arriba para evitar NaNs en el sombreado
    NORMAL = vec3(0.0, 1.0, 0.0);
}

void fragment() {
    vec4 id_sample = texture(id_map, v_uv);
    int node_id = int(round(id_sample.r * 255.0)) + 
                  (int(round(id_sample.g * 255.0)) * 256) + 
                  (int(round(id_sample.b * 255.0)) * 65536);
    
    int array_idx = node_id - 1;
    ivec2 data_coords = ivec2(array_idx % 4096, array_idx / 4096);
    
    // 1. Color Base (Satélite) - Protegido
    vec3 col_base = texture(albedo_tex, v_uv).rgb;
    col_base = clamp(col_base * brightness, 0.0, 1.0);
    col_base = mix(vec3(0.5), col_base, saturation);
    
    // 2. Detalle de Texturas (Solo si están asignadas y no estamos en modo político)
    float h = v_height;
    vec2 detail_uv = v_uv * detail_scale;
    
    if (map_mode == 3 && h > 0.01) {
        float rock_mask = smoothstep(0.4, 0.6, h);
        float veg_mask = smoothstep(0.05, 0.2, col_base.g - col_base.r);
        
        vec3 grass = texture(grass_albedo, detail_uv).rgb;
        vec3 rock = texture(rock_albedo, detail_uv).rgb;
        
        // Mezcla de detalle muy sutil para no "quemar" el blanco
        vec3 detail = mix(grass, rock, rock_mask);
        col_base = mix(col_base, col_base * detail * 1.2, detail_strength * 0.5);
    }

    // 3. Modos Políticos
    vec3 final_color = col_base;
    if (map_mode != 3) {
        vec3 col_pol;
        if (map_mode == 1) col_pol = texelFetch(country_lookup, data_coords, 0).rgb;
        else if (map_mode == 2) col_pol = texelFetch(state_lookup, data_coords, 0).rgb;
        else col_pol = vec3(h);
        
        final_color = mix(col_base, col_pol, 0.4);
    }

    // 4. Bordes de Seguridad
    ivec2 ts = textureSize(id_map, 0);
    if (ts.x > 0 && (map_mode == 1 || map_mode == 2)) {
        vec2 ps = 1.0 / vec2(ts);
        vec4 n1 = texture(id_map, v_uv + vec2(ps.x, 0.0) * 2.0);
        if (distance(id_sample, n1) > 0.01) {
            final_color = mix(final_color, vec3(1.0), 0.5);
        }
    }

    ALBEDO = clamp(final_color, vec3(0.0), vec3(1.0));
    ROUGHNESS = 0.8;
}
