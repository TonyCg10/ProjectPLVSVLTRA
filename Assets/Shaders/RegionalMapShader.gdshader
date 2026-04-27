shader_type spatial;
render_mode cull_back, diffuse_burley, specular_schlick_ggx;

// =========================================
// TEXTURAS GLOBALES
// =========================================
uniform sampler2D id_map : filter_nearest, hint_default_black;
uniform sampler2D country_lookup : filter_nearest;
uniform sampler2D state_lookup : filter_nearest;
uniform sampler2D height_map : filter_linear;
uniform sampler2D albedo_tex : source_color;
uniform sampler2D normal_tex : hint_normal;
uniform sampler2D water_map : filter_linear;
uniform sampler2D world_data_lookup;
uniform sampler2D splat_map : source_color;

// =========================================
// RECORTE DE PAÍS
// =========================================
uniform vec2 country_uv_min = vec2(0.0, 0.0);
uniform vec2 country_uv_max = vec2(1.0, 1.0);
uniform vec3 target_country_color = vec3(-1.0);
uniform float focus_radius : hint_range(0.1, 0.8) = 0.42;
uniform float focus_feather : hint_range(0.01, 0.3) = 0.12;

// =========================================
// CONFIGURACIÓN
// =========================================
uniform int map_mode : hint_range(0, 3) = 3;
uniform float height_scale : hint_range(0.0, 50.0) = 15.0;
uniform bool use_height_displacement = true;

// =========================================
// TEXTURAS DE TERRENO
// =========================================
uniform sampler2D grass_albedo : source_color, hint_default_white, repeat_enable, filter_linear_mipmap_anisotropic;
uniform sampler2D grass_normal : hint_normal, repeat_enable, filter_linear_mipmap_anisotropic;
uniform sampler2D sand_albedo : source_color, hint_default_white, repeat_enable, filter_linear_mipmap_anisotropic;
uniform sampler2D sand_normal : hint_normal, repeat_enable, filter_linear_mipmap_anisotropic;
uniform sampler2D rock_albedo : source_color, hint_default_white, repeat_enable, filter_linear_mipmap_anisotropic;
uniform sampler2D rock_normal : hint_normal, repeat_enable, filter_linear_mipmap_anisotropic;

uniform float detail_scale : hint_range(10.0, 5000.0) = 600.0;
uniform sampler2D detail_displacement : repeat_enable, filter_linear_mipmap_anisotropic;
uniform float detail_displacement_scale : hint_range(0.0, 10.0) = 1.0;
uniform float procedural_biome_strength : hint_range(0.0, 1.0) = 0.75;
uniform float detail_contrast : hint_range(0.5, 2.5) = 1.25;
uniform float satellite_tint_strength : hint_range(0.0, 1.0) = 0.85;
uniform float macro_variation_strength : hint_range(0.0, 0.35) = 0.12;
uniform float overview_blur_px : hint_range(0.0, 6.0) = 2.0;
uniform float overview_dim : hint_range(0.0, 1.0) = 0.2;
uniform bool enable_global_overview = false;
uniform vec3 fog_color : source_color = vec3(0.35, 0.45, 0.55);
uniform float fog_strength : hint_range(0.0, 1.0) = 0.2;
uniform float political_edge_aa : hint_range(0.0, 2.0) = 1.0;
uniform float coastline_aa : hint_range(0.0, 3.0) = 1.25;
uniform vec3 ocean_deep_color : source_color = vec3(0.05, 0.16, 0.30);
uniform vec3 ocean_shallow_color : source_color = vec3(0.24, 0.42, 0.56);
uniform vec3 border_color_country : source_color = vec3(0.17, 0.16, 0.13);
uniform vec3 border_color_state : source_color = vec3(0.24, 0.21, 0.18);

// Selección
uniform int selected_country_idx = -1;
uniform int selected_state_idx = -1;
uniform vec3 selection_color = vec3(1.0, 1.0, 1.0);

// Transición
uniform float portal_fade : hint_range(0.0, 1.0) = 0.0;

group_uniforms Effects;
uniform float brightness_mult : hint_range(0.0, 5.0) = 1.4;
uniform float peak_intensity : hint_range(0.0, 2.0) = 0.15;
uniform float fresnel_strength : hint_range(0.0, 1.0) = 0.1;

varying float v_height;
varying vec2 v_world_uv;
varying vec2 v_local_uv;
varying float v_water;
varying vec3 v_world_pos;

float hash12(vec2 p) {
    return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453123);
}

float noise2d(vec2 p) {
    vec2 i = floor(p);
    vec2 f = fract(p);
    vec2 u = f * f * (3.0 - 2.0 * f);
    float a = hash12(i + vec2(0.0, 0.0));
    float b = hash12(i + vec2(1.0, 0.0));
    float c = hash12(i + vec2(0.0, 1.0));
    float d = hash12(i + vec2(1.0, 1.0));
    return mix(mix(a, b, u.x), mix(c, d, u.x), u.y);
}

vec3 remap_contrast(vec3 c, float k) {
    return clamp((c - 0.5) * k + 0.5, 0.0, 1.0);
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

vec3 sample_blur5(sampler2D tex, vec2 uv, vec2 px, float radius_px) {
    if (radius_px <= 0.001) return texture(tex, uv).rgb;
    vec2 d = px * radius_px;
    vec3 c = texture(tex, uv).rgb * 0.34;
    c += texture(tex, uv + vec2(d.x, 0.0)).rgb * 0.165;
    c += texture(tex, uv - vec2(d.x, 0.0)).rgb * 0.165;
    c += texture(tex, uv + vec2(0.0, d.y)).rgb * 0.165;
    c += texture(tex, uv - vec2(0.0, d.y)).rgb * 0.165;
    return c;
}

void vertex() {
    v_local_uv = UV;
    v_world_uv = mix(country_uv_min, country_uv_max, UV);
    vec2 detail_uv = v_local_uv * detail_scale;
    v_height = texture(height_map, v_world_uv).r;
    v_water = texture(water_map, v_world_uv).r;

    float land_h = 0.0;
    float current_h = 0.0;
    
    if (use_height_displacement) {
        float land_mask_v = 1.0 - step(0.5, v_water);
        land_h = v_height * land_mask_v;
        float detail_h = texture(detail_displacement, detail_uv).r * detail_displacement_scale * land_mask_v;
        current_h = (land_h * height_scale) + detail_h;
        VERTEX.y += current_h;

        // Normales dinámicas para relieve PBR (only when shader controls displacement)
        // When using a physical ArrayMesh, the mesh provides its own vertex normals.
        float d = 0.002;
        
        float water_right = texture(water_map, v_world_uv + vec2(d, 0.0)).r;
        float land_mask_right = 1.0 - step(0.5, water_right);
        float h_right_base = texture(height_map, v_world_uv + vec2(d, 0.0)).r;
        float h_right_det  = texture(detail_displacement, detail_uv + vec2(d * detail_scale, 0.0)).r * detail_displacement_scale * land_mask_right;
        float h_right = (h_right_base * land_mask_right * height_scale) + h_right_det;
        
        float water_up = texture(water_map, v_world_uv + vec2(0.0, d)).r;
        float land_mask_up = 1.0 - step(0.5, water_up);
        float h_up_base    = texture(height_map, v_world_uv + vec2(0.0, d)).r;
        float h_up_det     = texture(detail_displacement, detail_uv + vec2(0.0, d * detail_scale)).r * detail_displacement_scale * land_mask_up;
        float h_up    = (h_up_base * land_mask_up * height_scale) + h_up_det;

        float world_d = d * 540.0; 
        vec3 tangent = normalize(vec3(world_d, h_right - current_h, 0.0));
        vec3 binormal = normalize(vec3(0.0, h_up - current_h, world_d));
        NORMAL = normalize(cross(binormal, tangent));
    }
    
    v_world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
}

void fragment() {
    vec2 overview_uv = UV;
    float center_dist = distance(overview_uv, vec2(0.5));
    float focus_mask = 1.0 - smoothstep(focus_radius, focus_radius + focus_feather, center_dist);

    // 1. Muestreo de Identidad y Agua
    float warped_water = texture(water_map, v_world_uv).r;
    bool is_water = warped_water > 0.16;
    
    vec4 id_sample = texture(id_map, v_world_uv);
    int node_id = decode_node_id(id_sample.rgb);

    // [NUEVO] Corregir UVs usando world_data_tex (Geo-correction)
    vec2 corrected_uv = v_world_uv;
    if (node_id > 0) {
        vec4 geo_data = texelFetch(world_data_lookup, node_coords(node_id), 0);
        // U_geo = (Byte0 - 48) / 207.0, V_geo = (Byte2 - 76) / 179.0
        // Solo aplicamos si los datos son válidos (Byte0 >= 48)
        if (geo_data.r * 255.0 >= 47.0) {
            corrected_uv = vec2((geo_data.r * 255.0 - 48.0) / 207.0, (geo_data.b * 255.0 - 76.0) / 179.0);
            corrected_uv = clamp(corrected_uv, 0.0, 1.0);
        }
    }
    
    // 2. Political borders — compare LOOKUP colors, not raw node IDs
    // Usamos corrected_uv para que los bordes sigan a la geografía corregida
    ivec2 id_ts = textureSize(id_map, 0);
    vec2 id_ps = 1.0 / vec2(id_ts);

    // 4-tap subpixel supersampling for smoother political contours.
    vec2 aa_off = id_ps * (0.5 * political_edge_aa);
    vec2 suv0 = v_world_uv + vec2(-aa_off.x, -aa_off.y);
    vec2 suv1 = v_world_uv + vec2( aa_off.x, -aa_off.y);
    vec2 suv2 = v_world_uv + vec2(-aa_off.x,  aa_off.y);
    vec2 suv3 = v_world_uv + vec2( aa_off.x,  aa_off.y);

    ivec2 c0 = node_coords(decode_node_id(texture(id_map, suv0).rgb));
    ivec2 c1 = node_coords(decode_node_id(texture(id_map, suv1).rgb));
    ivec2 c2 = node_coords(decode_node_id(texture(id_map, suv2).rgb));
    ivec2 c3 = node_coords(decode_node_id(texture(id_map, suv3).rgb));

    vec3 cc0 = texelFetch(country_lookup, c0, 0).rgb;
    vec3 cc1 = texelFetch(country_lookup, c1, 0).rgb;
    vec3 cc2 = texelFetch(country_lookup, c2, 0).rgb;
    vec3 cc3 = texelFetch(country_lookup, c3, 0).rgb;
    vec3 my_country_col = (cc0 + cc1 + cc2 + cc3) * 0.25;

    vec3 sc0 = texelFetch(state_lookup, c0, 0).rgb;
    vec3 sc1 = texelFetch(state_lookup, c1, 0).rgb;
    vec3 sc2 = texelFetch(state_lookup, c2, 0).rgb;
    vec3 sc3 = texelFetch(state_lookup, c3, 0).rgb;
    vec3 my_state_col = (sc0 + sc1 + sc2 + sc3) * 0.25;

    float c_var = max(max(distance(cc0, cc1), distance(cc0, cc2)), distance(cc0, cc3));
    float s_var = max(max(distance(sc0, sc1), distance(sc0, sc2)), distance(sc0, sc3));
    // Slightly wider thresholds reduce false-positive single-pixel dark seams.
    float country_border = smoothstep(0.03, 0.12, c_var);
    float state_border = smoothstep(0.03, 0.12, s_var);

    // 3. Satellite base (low-res, used ONLY as color tint reference)
    vec3 satellite_focus = texture(albedo_tex, corrected_uv).rgb;
    vec2 world_px = 1.0 / vec2(textureSize(albedo_tex, 0));
    vec3 satellite_overview = sample_blur5(albedo_tex, overview_uv, world_px, overview_blur_px);
    
    // 4. PBR Detail Terrain (high-res, tiled — this is the MAIN visual)
    vec2 detail_uv = v_local_uv * detail_scale;
    vec3 splat = texture(splat_map, v_world_uv).rgb;
    
    // Multi-octave detail UV to break tiling repetition
    vec2 detail_uv2 = detail_uv * 0.37 + vec2(17.3, 5.7); 
    vec2 detail_uv3 = detail_uv * 0.13 + vec2(3.1, 11.9); 
    
    vec3 tex_grass = texture(grass_albedo, detail_uv).rgb;
    vec3 tex_sand  = texture(sand_albedo,  detail_uv).rgb;
    vec3 tex_rock  = texture(rock_albedo,  detail_uv).rgb;
    
    vec3 tex_grass2 = texture(grass_albedo, detail_uv2).rgb;
    vec3 tex_sand2  = texture(sand_albedo,  detail_uv2).rgb;
    vec3 tex_rock2  = texture(rock_albedo,  detail_uv2).rgb;
    
    tex_grass = mix(tex_grass, tex_grass2, 0.35);
    tex_sand  = mix(tex_sand,  tex_sand2,  0.35);
    tex_rock  = mix(tex_rock,  tex_rock2,  0.35);
    
    // Procedural biome fallback to avoid blocky low-res splat maps in close zoom.
    float h = v_height;
    float moisture = 1.0 - v_water;
    float n0 = noise2d(v_world_uv * 12.0);
    float n1 = noise2d(v_world_uv * 38.0);
    float biome_noise = (n0 * 0.65 + n1 * 0.35) * 0.22 - 0.11;

    float w_grass_p = clamp(1.0 - abs(h - 0.42) * 3.2 + moisture * 0.45 + biome_noise, 0.0, 1.0);
    float w_sand_p  = clamp((1.0 - moisture) * 0.9 + (0.28 - h) * 1.7 + biome_noise * 0.5, 0.0, 1.0);
    float w_rock_p  = clamp((h - 0.5) * 2.2 + (1.0 - moisture) * 0.15 - biome_noise * 0.4, 0.0, 1.0);
    float p_sum = max(w_grass_p + w_sand_p + w_rock_p, 0.0001);
    vec3 proc_weights = vec3(w_grass_p, w_sand_p, w_rock_p) / p_sum;

    vec3 mixed_weights = mix(splat, proc_weights, procedural_biome_strength);
    float total_w = mixed_weights.r + mixed_weights.g + mixed_weights.b;
    vec3 terrain_detail;
    if (total_w < 0.02) {
        terrain_detail = tex_grass; // Fallback to grass if unpainted
    } else {
        terrain_detail = (tex_grass * mixed_weights.r + tex_sand * mixed_weights.g + tex_rock * mixed_weights.b) / (total_w + 0.0001);
    }
    
    // === TERRAIN COLORING ===
    // Satellite image provides continuous, geographically-accurate color.
    // PBR detail textures add only subtle micro-surface variation (no visible tiling).
    terrain_detail = remap_contrast(terrain_detail, detail_contrast);
    
    // Extract luminance-neutral detail pattern (texture variation without color shift)
    float detail_lum = dot(terrain_detail, vec3(0.299, 0.587, 0.114));
    vec3 detail_neutral = terrain_detail / max(detail_lum, 0.001);
    
    // Satellite is the dominant color; PBR detail adds surface micro-texture
    float detail_influence = (1.0 - satellite_tint_strength) * 0.4;
    vec3 tinted_terrain = satellite_focus * mix(vec3(1.0), detail_neutral, detail_influence);
    
    // Macro variation to break any residual patterns
    float macro = noise2d(v_world_uv * 34.0) * 2.0 - 1.0;
    vec3 macro_tint = vec3(1.0 + macro * macro_variation_strength);
    tinted_terrain *= macro_tint;
    
    // === COASTLINE & WATER TRANSITION ===
    float water_w = max(fwidth(warped_water) * coastline_aa, 0.003);
    float water_blend = smoothstep(0.08 - water_w, 0.28 + water_w, warped_water);
    
    // Beach/sand zone: appears between dry land and water
    float beach_inner = smoothstep(0.02, 0.08, warped_water);
    float beach_outer = 1.0 - smoothstep(0.12, 0.28, warped_water);
    float beach_zone = beach_inner * beach_outer;
    vec3 beach_color = mix(vec3(0.82, 0.76, 0.60), satellite_focus * 1.1, 0.25);
    tinted_terrain = mix(tinted_terrain, beach_color, beach_zone * 0.55);
    
    // Shoreline detection for foam/highlights
    vec2 water_ps = 1.0 / vec2(textureSize(water_map, 0));
    float w_right = texture(water_map, v_world_uv + vec2(water_ps.x, 0.0)).r;
    float w_up = texture(water_map, v_world_uv + vec2(0.0, water_ps.y)).r;
    float shoreline = clamp((abs(warped_water - w_right) + abs(warped_water - w_up)) * 8.0, 0.0, 1.0);

    // Shallow water blending near coast (terrain slopes into ocean)
    vec3 water_base = mix(ocean_shallow_color, ocean_deep_color, smoothstep(0.2, 0.6, warped_water));
    vec3 final_terrain_focus = mix(tinted_terrain, water_base, water_blend * 0.85);
    
    // Overview (distant background) stays satellite-based since it's small anyway
    vec3 final_terrain_overview = satellite_overview * (1.0 - overview_dim * 0.3);
    final_terrain_overview = mix(final_terrain_overview, fog_color, fog_strength * (1.0 - focus_mask));
    
    // 5. Modos de Mapa
    vec3 final_color = final_terrain_focus;
    float land_mask = 1.0 - water_blend;
    
    // Patrón discontinuo en espacio de pantalla (líneas finas, separadas)
    float dash = step(0.65, fract((FRAGCOORD.x - FRAGCOORD.y) * 0.08));
    
    if (map_mode == 1) {
        final_color = mix(final_color, vec3(1.0), country_border * dash * land_mask);
    } else if (map_mode == 2) {
        final_color = mix(final_color, vec3(1.0), state_border * dash * land_mask);
        final_color = mix(final_color, vec3(1.0), country_border * dash * land_mask);
    } else if (map_mode == 3) {
        final_color = mix(final_color, border_color_country, country_border * 0.20 * land_mask);
    }

    // Shoreline highlights
    vec3 water_highlight = mix(final_color, vec3(0.85, 0.90, 0.95), shoreline * 0.45);
    vec3 land_highlight = mix(final_color, vec3(0.92, 0.92, 0.86), shoreline * 0.15);
    final_color = mix(land_highlight, water_highlight, water_blend);

    // 7. Mix overview only when explicitly enabled.
    vec3 composed = final_color;
    if (enable_global_overview) {
        composed = mix(final_terrain_overview, final_color, focus_mask);
        composed = mix(composed, composed * (1.0 - overview_dim), 1.0 - focus_mask);
    }
    vec3 out_col = composed * brightness_mult;
    
    // Picos de montaña
    float peaks = smoothstep(0.35, 0.6, v_height);
    out_col += peaks * peak_intensity * 0.5;

    // 8. Output Final
    ALBEDO = mix(out_col, vec3(0.0), portal_fade);
    
    METALLIC = 0.0;
    ROUGHNESS = mix(0.65, 0.05, water_blend);
    SPECULAR = mix(0.15, 0.9, water_blend);
    
    // PBR Normal maps from detail textures (subtle surface depth)
    if (focus_mask > 0.1 && water_blend < 0.8) {
        vec2 dnuv = v_local_uv * detail_scale;
        vec3 n_grass = texture(grass_normal, dnuv).rgb;
        vec3 n_sand  = texture(sand_normal,  dnuv).rgb;
        vec3 n_rock  = texture(rock_normal,  dnuv).rgb;
        vec3 blended_normal = (n_grass * mixed_weights.r + n_sand * mixed_weights.g + n_rock * mixed_weights.b) / (total_w + 0.0001);
        
        NORMAL_MAP = mix(blended_normal, vec3(0.5, 0.5, 1.0), water_blend);
        NORMAL_MAP_DEPTH = 0.6 * focus_mask * (1.0 - water_blend);
    }
    
    // Discard only deep water — coastline is handled by terrain geometry sloping into ocean
    if (water_blend > 0.92) {
        discard;
    }
    
    ALPHA = 1.0;
}
