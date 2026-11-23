shader_type spatial;

// Ground: watered grass near the circuit fading to desert sand, using a baked
// distance-to-track mask (R = distance / 300 m) laid over the track's bounding square.
uniform sampler2D grass_tex : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D grass_n : hint_normal, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D sand_tex : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D sand_n : hint_normal, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D dist_mask : filter_linear, repeat_disable;
uniform sampler2D noise : filter_linear_mipmap, repeat_enable;
uniform bool use_tex = false;
uniform vec4 mask_rect; // x0, z0, size, unused
uniform float grass_m = 45.0;

varying vec3 wpos;

void vertex() {
	wpos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
}

void fragment() {
	vec2 muv = (wpos.xz - mask_rect.xy) / mask_rect.z;
	float d = texture(dist_mask, muv).r * 300.0;
	float wobble = (texture(noise, wpos.xz * 0.01).r - 0.5) * 30.0;
	float g = 1.0 - smoothstep(grass_m - 10.0, grass_m + 10.0, d + wobble);
	float var = texture(noise, wpos.xz * 0.002).r;
	vec3 grass, sand;
	vec3 gn, sn;
	if (use_tex) {
		vec2 p = wpos.xz / 3.0, s = wpos.xz / 6.0;
		grass = texture(grass_tex, p).rgb * mix(0.8, 1.15, var);
		sand = texture(sand_tex, s).rgb * mix(0.9, 1.08, var);
		gn = texture(grass_n, p).rgb;
		sn = texture(sand_n, s).rgb;
	} else {
		grass = vec3(0.2, 0.32, 0.1) * mix(0.75, 1.2, var);
		sand = vec3(0.76, 0.64, 0.45) * mix(0.9, 1.08, var);
		gn = vec3(0.5, 0.5, 1.0);
		sn = vec3(0.5, 0.5, 1.0);
	}
	ALBEDO = mix(sand, grass, g);
	ROUGHNESS = mix(0.95, 0.85, g);
	NORMAL_MAP = mix(sn, gn, g);
}
