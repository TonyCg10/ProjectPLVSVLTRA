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
uniform sampler2DArray world_data_tex;
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
uniform sampler2D grass_albedo : source_color, hint_default_white;
uniform sampler2D grass_normal : hint_normal;
uniform sampler2D sand_albedo : source_color, hint_default_white;
uniform sampler2D sand_normal : hint_normal;
uniform sampler2D rock_albedo : source_color, hint_default_white;
uniform sampler2D rock_normal : hint_normal;

uniform float detail_scale : hint_range(10.0, 5000.0) = 600.0;
uniform sampler2D detail_displacement : filter_linear;
uniform float detail_displacement_scale : hint_range(0.0, 10.0) = 1.0;
uniform float overview_blur_px : hint_range(0.0, 6.0) = 2.0;
uniform float overview_dim : hint_range(0.0, 1.0) = 0.2;
uniform vec3 fog_color : source_color = vec3(0.35, 0.45, 0.55);
uniform float fog_strength : hint_range(0.0, 1.0) = 0.2;

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
    v_height = texture(height_map, v_world_uv).r;
    v_water = texture(water_map, v_world_uv).r;

    float land_h = 0.0;
    float current_h = 0.0;
    
    if (use_height_displacement) {
        land_h = v_height * (1.0 - step(0.5, v_water));
        float detail_h = texture(detail_displacement, UV).r * detail_displacement_scale;
        current_h = (land_h * height_scale) + detail_h;
        VERTEX.y += current_h;
    }

    // Normales dinámicas para relieve PBR
    float d = 0.002;
    
    float h_right_base = texture(height_map, v_world_uv + vec2(d, 0.0)).r;
    float h_right_det  = texture(detail_displacement, UV + vec2(d, 0.0)).r * detail_displacement_scale;
    float h_right = (h_right_base * (1.0 - step(0.5, texture(water_map, v_world_uv + vec2(d, 0.0)).r)) * height_scale) + h_right_det;
    
    float h_up_base    = texture(height_map, v_world_uv + vec2(0.0, d)).r;
    float h_up_det     = texture(detail_displacement, UV + vec2(0.0, d)).r * detail_displacement_scale;
    float h_up    = (h_up_base * (1.0 - step(0.5, texture(water_map, v_world_uv + vec2(0.0, d)).r)) * height_scale) + h_up_det;

    float world_d = d * 540.0; 
    vec3 tangent = normalize(vec3(world_d, h_right - current_h, 0.0));
    vec3 binormal = normalize(vec3(0.0, h_up - current_h, world_d));
    NORMAL = normalize(cross(binormal, tangent));
    
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
    int node_id = int(round(id_sample.r * 255.0)) +
                  (int(round(id_sample.g * 255.0)) * 256) +
                  (int(round(id_sample.b * 255.0)) * 65536);

    int array_idx = node_id - 1;
    ivec2 data_coords = ivec2(array_idx % 4096, array_idx / 4096);
    
    // Aislamiento de País (Opcional, para efectos específicos)
    bool inside_country = (target_country_color.x < 0.0) || (distance(id_sample.rgb, target_country_color) < 0.01);

    // 2. Political borders — compare LOOKUP colors, not raw node IDs
    ivec2 id_ts = textureSize(id_map, 0);
    vec2 id_ps = 1.0 / vec2(id_ts);

    vec3 my_country_col = texelFetch(country_lookup, data_coords, 0).rgb;
    vec3 my_state_col = texelFetch(state_lookup, data_coords, 0).rgb;

    float country_border = 0.0;
    float state_border = 0.0;

    // Check right and up neighbors
    vec4 nb_r = texture(id_map, v_world_uv + vec2(id_ps.x * 1.5, 0.0));
    int nid_r = int(round(nb_r.r * 255.0)) + (int(round(nb_r.g * 255.0)) * 256) + (int(round(nb_r.b * 255.0)) * 65536);
    ivec2 nc_r = ivec2((nid_r - 1) % 4096, (nid_r - 1) / 4096);

    vec4 nb_u = texture(id_map, v_world_uv + vec2(0.0, id_ps.y * 1.5));
    int nid_u = int(round(nb_u.r * 255.0)) + (int(round(nb_u.g * 255.0)) * 256) + (int(round(nb_u.b * 255.0)) * 65536);
    ivec2 nc_u = ivec2((nid_u - 1) % 4096, (nid_u - 1) / 4096);

    vec3 nb_c_r = texelFetch(country_lookup, nc_r, 0).rgb;
    vec3 nb_c_u = texelFetch(country_lookup, nc_u, 0).rgb;
    if (distance(my_country_col, nb_c_r) > 0.02 || distance(my_country_col, nb_c_u) > 0.02)
        country_border = 1.0;

    vec3 nb_s_r = texelFetch(state_lookup, nc_r, 0).rgb;
    vec3 nb_s_u = texelFetch(state_lookup, nc_u, 0).rgb;
    if (distance(my_state_col, nb_s_r) > 0.02 || distance(my_state_col, nb_s_u) > 0.02)
        state_border = 1.0;

    // 3. Satellite base (low-res, used ONLY as color tint reference)
    vec3 satellite_focus = texture(albedo_tex, v_world_uv).rgb;
    vec2 world_px = 1.0 / vec2(textureSize(albedo_tex, 0));
    vec3 satellite_overview = sample_blur5(albedo_tex, overview_uv, world_px, overview_blur_px);
    
    // 4. PBR Detail Terrain (high-res, tiled — this is the MAIN visual)
    vec2 detail_uv = v_local_uv * detail_scale;
    vec3 splat = texture(splat_map, v_world_uv).rgb;
    
    // Multi-octave detail UV to break tiling repetition
    vec2 detail_uv2 = detail_uv * 0.37 + vec2(17.3, 5.7);  // offset second octave
    vec2 detail_uv3 = detail_uv * 0.13 + vec2(3.1, 11.9);   // third octave (macro variation)
    
    vec3 tex_grass = texture(grass_albedo, detail_uv).rgb;
    vec3 tex_sand  = texture(sand_albedo,  detail_uv).rgb;
    vec3 tex_rock  = texture(rock_albedo,  detail_uv).rgb;
    
    // Second octave for anti-tiling
    vec3 tex_grass2 = texture(grass_albedo, detail_uv2).rgb;
    vec3 tex_sand2  = texture(sand_albedo,  detail_uv2).rgb;
    vec3 tex_rock2  = texture(rock_albedo,  detail_uv2).rgb;
    
    // Blend two octaves to kill visible repetition
    tex_grass = mix(tex_grass, tex_grass2, 0.35);
    tex_sand  = mix(tex_sand,  tex_sand2,  0.35);
    tex_rock  = mix(tex_rock,  tex_rock2,  0.35);
    
    float total_w = splat.r + splat.g + splat.b + 0.0001;
    vec3 terrain_detail = (tex_grass * splat.r + tex_sand * splat.g + tex_rock * splat.b) / total_w;
    
    // Use satellite as a subtle color tint — NOT as the base image
    // Extract the satellite's hue/tone and apply it gently to the PBR terrain
    vec3 sat_tint = satellite_focus / max(dot(satellite_focus, vec3(0.33)), 0.1); // Normalize brightness
    sat_tint = clamp(sat_tint, 0.3, 2.0); // Prevent extreme values
    
    // Final focused terrain: PBR dominant (85%), satellite color-tint only (15%)
    vec3 tinted_terrain = terrain_detail * mix(vec3(1.0), sat_tint, 0.15);
    vec3 final_terrain_focus = mix(satellite_focus, tinted_terrain, step(0.1, total_w) * 0.85 + 0.10);
    
    // Overview (distant background) stays satellite-based since it's small anyway
    vec3 final_terrain_overview = satellite_overview * (1.0 - overview_dim * 0.3);
    final_terrain_overview = mix(final_terrain_overview, fog_color, fog_strength * (1.0 - focus_mask));
    
    // 5. Modos de Mapa
    vec3 final_color = final_terrain_focus;
    if (map_mode == 1) {
        final_color = mix(final_color, my_country_col, 0.55);
        if (country_border > 0.5) final_color = mix(final_color, vec3(0.05), 0.6);
    } else if (map_mode == 2) {
        final_color = mix(final_color, my_state_col, 0.55);
        if (state_border > 0.5) final_color = mix(final_color, vec3(0.1, 0.08, 0.06), 0.5);
        if (country_border > 0.5) final_color = mix(final_color, vec3(0.05), 0.7);
    } else if (map_mode == 3) {
        // Natural mode — show borders subtly
        if (country_border > 0.5) final_color = mix(final_color, vec3(0.1), 0.3);
    }

    // 6. Costa/agua con interacción visual suave
    vec2 water_ps = 1.0 / vec2(textureSize(water_map, 0));
    float w_right = texture(water_map, v_world_uv + vec2(water_ps.x, 0.0)).r;
    float w_up = texture(water_map, v_world_uv + vec2(0.0, water_ps.y)).r;
    float shoreline = (abs(warped_water - w_right) + abs(warped_water - w_up)) * 8.0;
    shoreline = clamp(shoreline, 0.0, 1.0);
    if (is_water) {
        final_color = mix(final_color, vec3(0.78, 0.86, 0.92), shoreline * 0.55);
    } else {
        final_color = mix(final_color, vec3(0.92, 0.92, 0.86), shoreline * 0.2);
    }

    // 7. Mezcla: mundo completo difuminado + región enfocada nítida
    vec3 composed = mix(final_terrain_overview, final_color, focus_mask);
    composed = mix(composed, composed * (1.0 - overview_dim), 1.0 - focus_mask);
    vec3 out_col = composed * brightness_mult;
    
    // Picos de montaña
    float peaks = smoothstep(0.35, 0.6, v_height);
    out_col += peaks * peak_intensity * 0.5;

    // 8. Output Final
    ALBEDO = mix(out_col, vec3(0.0), portal_fade);
    
    METALLIC = 0.0;
    ROUGHNESS = is_water ? 0.05 : 0.65;
    SPECULAR = is_water ? 0.9 : 0.15;
    
    // PBR Normal maps from detail textures (adds surface depth and breaks flatness)
    if (!is_water && focus_mask > 0.1) {
        vec2 dnuv = v_local_uv * detail_scale;
        vec3 n_grass = texture(grass_normal, dnuv).rgb;
        vec3 n_sand  = texture(sand_normal,  dnuv).rgb;
        vec3 n_rock  = texture(rock_normal,  dnuv).rgb;
        vec3 blended_normal = (n_grass * splat.r + n_sand * splat.g + n_rock * splat.b) / total_w;
        NORMAL_MAP = blended_normal;
        NORMAL_MAP_DEPTH = 0.8 * focus_mask;
    }
    
    if (is_water) {
        ALPHA = 0.3 + (0.7 * portal_fade);
    }
}
