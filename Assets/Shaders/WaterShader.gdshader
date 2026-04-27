// WaterShader.gdshader
// Agua realista con olas Gerstner, espuma procedural y luz PBR.
// No depende de screen_texture ni depth_texture (que fallan en muchas configs).
// Godot 4.x — render_mode spatial + diffuse_burley + specular_schlick_ggx

shader_type spatial;
render_mode blend_mix,
            depth_draw_opaque,
            cull_back,
            diffuse_burley,
            specular_schlick_ggx;

// ─────────────────────────────────────────────────────────────────────────────
// COLORES DEL AGUA
// ─────────────────────────────────────────────────────────────────────────────
uniform vec4  shallow_color  : source_color = vec4(0.08, 0.28, 0.42, 0.80);
uniform vec4  deep_color     : source_color = vec4(0.01, 0.06, 0.20, 0.92);
uniform vec4  foam_color     : source_color = vec4(0.85, 0.90, 0.95, 1.00);
uniform float depth_distance : hint_range(0.5, 30.0) = 8.0;

// ─────────────────────────────────────────────────────────────────────────────
// OLAS — parámetros de las 4 olas Gerstner superpuestas
// ─────────────────────────────────────────────────────────────────────────────
uniform float wave_speed      : hint_range(0.0, 5.0)  = 0.8;
uniform float wave_height     : hint_range(0.0, 8.0)  = 0.6;   // altura máxima (unidades Godot)
uniform float wave_steepness  : hint_range(0.0, 1.0)  = 0.55;  // 0=sinusoidal 1=muy puntiaguda
uniform float wave_length     : hint_range(1.0, 100.0)= 18.0;  // longitud de onda base

// ─────────────────────────────────────────────────────────────────────────────
// SUPERFICIE — normal maps para detalle visual (no afectan geometría)
// ─────────────────────────────────────────────────────────────────────────────
uniform sampler2D normal_map_a  : hint_normal, filter_linear_mipmap, repeat_enable;
uniform sampler2D normal_map_b  : hint_normal, filter_linear_mipmap, repeat_enable;
uniform float     normal_scale  : hint_range(1.0, 300.0) = 60.0;  // tiling
uniform float     normal_depth  : hint_range(0.0, 3.0)   = 1.4;   // intensidad

// ─────────────────────────────────────────────────────────────────────────────
// PBR
// ─────────────────────────────────────────────────────────────────────────────
uniform float roughness       : hint_range(0.0, 1.0) = 0.06;
uniform float metallic        : hint_range(0.0, 1.0) = 0.0;
uniform float specular        : hint_range(0.0, 1.0) = 0.92;

// ─────────────────────────────────────────────────────────────────────────────
// ESPUMA procedural en las crestas de las olas
// ─────────────────────────────────────────────────────────────────────────────
uniform float foam_threshold  : hint_range(0.0, 1.0) = 0.72;  // altura relativa que genera espuma
uniform float foam_softness   : hint_range(0.01, 0.5)= 0.18;

// ─────────────────────────────────────────────────────────────────────────────
// FADE para ocultar los clones del mapa que están lejos
// ─────────────────────────────────────────────────────────────────────────────
uniform float fade_start      : hint_range(100.0, 600.0) = 350.0;
uniform float fade_end        : hint_range(200.0, 800.0) = 550.0;

// ─────────────────────────────────────────────────────────────────────────────
// VARYINGS
// ─────────────────────────────────────────────────────────────────────────────
varying float v_wave_height_norm;  // [0,1] posición relativa de la ola
varying vec3  v_world_pos;
varying vec2  v_world_uv;


// ─────────────────────────────────────────────────────────────────────────────
// GERSTNER WAVE — una sola ola en dirección D con parámetros dados
// Retorna: .xyz = desplazamiento vertex, .w = altura normalizada [0,1]
// ─────────────────────────────────────────────────────────────────────────────
vec4 gerstner_wave(vec2 pos, vec2 dir, float amplitude, float steepness,
                   float wavelength, float speed, float time)
{
    float k       = TAU / wavelength;            // número de onda
    float c       = sqrt(9.8 / k);              // velocidad de fase (gravedad realista)
    float phase   = k * (dot(dir, pos) - c * speed * time);
    float Q       = steepness / (k * amplitude);  // factor de forma Gerstner

    float sinP = sin(phase);
    float cosP = cos(phase);

    vec3 disp;
    disp.x = Q * amplitude * dir.x * cosP;
    disp.z = Q * amplitude * dir.y * cosP;  // Y está como Z en Godot (Y = up)
    disp.y = amplitude * sinP;              // altura

    float height_norm = (sinP + 1.0) * 0.5;
    return vec4(disp, height_norm);
}


// ─────────────────────────────────────────────────────────────────────────────
// VERTEX
// ─────────────────────────────────────────────────────────────────────────────
void vertex()
{
    vec3 world = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
    vec2 xz    = world.xz;

    float t    = TIME * wave_speed;
    float amp  = wave_height;
    float st   = wave_steepness;
    float wl   = wave_length;

    // 4 olas superpuestas con distintas direcciones y longitudes
    vec4 w1 = gerstner_wave(xz, normalize(vec2(1.0,  0.6)), amp * 1.00, st,       wl,       t, 1.0);
    vec4 w2 = gerstner_wave(xz, normalize(vec2(0.4,  1.0)), amp * 0.65, st * 0.8, wl * 1.4, t, 1.0);
    vec4 w3 = gerstner_wave(xz, normalize(vec2(-0.7, 0.5)), amp * 0.45, st * 0.6, wl * 0.7, t, 1.0);
    vec4 w4 = gerstner_wave(xz, normalize(vec2(0.3, -0.8)), amp * 0.30, st * 0.5, wl * 2.0, t, 1.0);

    vec3 disp = w1.xyz + w2.xyz + w3.xyz + w4.xyz;

    VERTEX += disp;

    // Altura normalizada [0,1] para la espuma en crestas
    float h_norm = (w1.w * 0.5 + w2.w * 0.3 + w3.w * 0.15 + w4.w * 0.05);
    v_wave_height_norm = h_norm;

    v_world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
    v_world_uv  = xz;

    // ── Normales analíticas (derivadas de las olas Gerstner) ──────────────────
    // Para cada ola: tangent_x += (-Q*amp*dir.x²*sin, amp*dir.x*cos, -Q*amp*dir.x*dir.y*sin)
    // Simplificamos usando diferencias finitas (más robusto con múltiples olas)
    float eps = 0.5;
    vec2 xz_ex = xz + vec2(eps, 0.0);
    vec2 xz_ez = xz + vec2(0.0, eps);

    vec4 w1x = gerstner_wave(xz_ex, normalize(vec2(1.0, 0.6)),   amp*1.00, st,       wl,       t, 1.0);
    vec4 w2x = gerstner_wave(xz_ex, normalize(vec2(0.4, 1.0)),   amp*0.65, st*0.8,   wl*1.4,   t, 1.0);
    vec4 w3x = gerstner_wave(xz_ex, normalize(vec2(-0.7, 0.5)),  amp*0.45, st*0.6,   wl*0.7,   t, 1.0);
    vec4 w4x = gerstner_wave(xz_ex, normalize(vec2(0.3, -0.8)),  amp*0.30, st*0.5,   wl*2.0,   t, 1.0);
    vec3 dx = (w1x.xyz + w2x.xyz + w3x.xyz + w4x.xyz - disp) / eps;

    vec4 w1z = gerstner_wave(xz_ez, normalize(vec2(1.0, 0.6)),   amp*1.00, st,       wl,       t, 1.0);
    vec4 w2z = gerstner_wave(xz_ez, normalize(vec2(0.4, 1.0)),   amp*0.65, st*0.8,   wl*1.4,   t, 1.0);
    vec4 w3z = gerstner_wave(xz_ez, normalize(vec2(-0.7, 0.5)),  amp*0.45, st*0.6,   wl*0.7,   t, 1.0);
    vec4 w4z = gerstner_wave(xz_ez, normalize(vec2(0.3, -0.8)),  amp*0.30, st*0.5,   wl*2.0,   t, 1.0);
    vec3 dz = (w1z.xyz + w2z.xyz + w3z.xyz + w4z.xyz - disp) / eps;

    vec3 T = normalize(vec3(1.0 + dx.x, dx.y, dx.z));
    vec3 B = normalize(vec3(dz.x, dz.y, 1.0 + dz.z));
    NORMAL = normalize(cross(B, T));
}


// ─────────────────────────────────────────────────────────────────────────────
// FRAGMENT
// ─────────────────────────────────────────────────────────────────────────────
void fragment()
{
    float t = TIME * wave_speed;

    // ── Normal maps animados ───────────────────────────────────────────────────
    vec2 uv1 = v_world_uv / normal_scale + vec2(t * 0.04,  t * 0.02);
    vec2 uv2 = v_world_uv / normal_scale + vec2(-t * 0.03, t * 0.05) + vec2(0.5);

    vec3 n1 = texture(normal_map_a, uv1).rgb * 2.0 - 1.0;
    vec3 n2 = texture(normal_map_b, uv2).rgb * 2.0 - 1.0;
    vec3 detail_normal = normalize(n1 + n2);

    NORMAL_MAP       = detail_normal * 0.5 + 0.5;
    NORMAL_MAP_DEPTH = normal_depth;

    // ── Color del agua: mezcla shallow/deep según la altura de la ola ─────────
    // En crestas (v_wave_height_norm alto) = agua más somera/clara
    // En valles (v_wave_height_norm bajo)  = agua más profunda/oscura
    float depth_t = 1.0 - smoothstep(0.3, 0.8, v_wave_height_norm);
    vec3 water_color = mix(shallow_color.rgb, deep_color.rgb, depth_t);

    // ── Espuma en crestas ─────────────────────────────────────────────────────
    float foam_mask = smoothstep(foam_threshold - foam_softness,
                                 foam_threshold + foam_softness,
                                 v_wave_height_norm);

    // Añadir un ruido procedural más suave para la espuma (sin aliasing estático)
    vec2 noise_uv = v_world_uv * 0.8 + t * 0.4;
    float foam_noise = sin(noise_uv.x) * cos(noise_uv.y) * 0.5 + 0.5;
    foam_mask *= (0.6 + 0.4 * foam_noise);
    foam_mask  = clamp(foam_mask, 0.0, 1.0);

    vec3 final_color = mix(water_color, foam_color.rgb, foam_mask);
    float final_alpha = mix(mix(shallow_color.a, deep_color.a, depth_t),
                            foam_color.a, foam_mask);

    // ── Fade para los clones lejanos ──────────────────────────────────────────
    float dist_xz = distance(NODE_POSITION_WORLD.xz, CAMERA_POSITION_WORLD.xz);
    float fade    = 1.0 - smoothstep(fade_start, fade_end, dist_xz);
    final_alpha  *= fade;

    // ── Output PBR ────────────────────────────────────────────────────────────
    ALBEDO    = final_color;
    ALPHA     = final_alpha;
    ROUGHNESS = mix(roughness, 0.6, foam_mask);   // espuma más rugosa
    METALLIC  = metallic;
    SPECULAR  = mix(specular, 0.1, foam_mask);    // espuma menos especular
}
