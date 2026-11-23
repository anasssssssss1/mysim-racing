shader_type spatial;

// World-space asphalt: scanned texture (when present) broken up by large-scale variation so it
// never visibly tiles, plus a rubbered, slightly glossier racing band (UV.y = 0..1 across the road).
uniform sampler2D albedo_tex : source_color, hint_default_white, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D normal_tex : hint_normal, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D rough_tex : hint_default_white, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D macro : hint_default_white, filter_linear_mipmap, repeat_enable;
uniform bool use_tex = false;
uniform float tile_m = 4.0;
uniform vec3 base_color : source_color = vec3(0.13, 0.13, 0.135);
uniform float rubber = 0.3;

varying vec3 wpos;

void vertex() {
	wpos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
}

void fragment() {
	vec2 p = wpos.xz / tile_m;
	// rotate a second sample to hide repeats
	vec2 q = mat2(vec2(0.8, -0.6), vec2(0.6, 0.8)) * p * 0.37 + vec2(0.31, 0.77);
	float big = texture(macro, wpos.xz * 0.004).r;
	float blend = texture(macro, wpos.xz * 0.03).r;
	vec3 c;
	float r;
	vec3 n;
	if (use_tex) {
		c = mix(texture(albedo_tex, p).rgb, texture(albedo_tex, q).rgb, smoothstep(0.35, 0.65, blend));
		r = mix(texture(rough_tex, p).r, texture(rough_tex, q).r, smoothstep(0.35, 0.65, blend));
		n = texture(normal_tex, p).rgb;
	} else {
		c = base_color * mix(0.7, 1.25, texture(macro, wpos.xz * 1.9).r);
		r = 0.9;
		n = vec3(0.5, 0.5, 1.0);
	}
	float band = 1.0 - smoothstep(0.1, 0.34, abs(UV.y - 0.5));
	c *= mix(0.86, 1.1, big);
	c *= 1.0 - rubber * band;
	ALBEDO = c;
	ROUGHNESS = clamp(r - 0.12 * band, 0.05, 1.0);
	SPECULAR = 0.4;
	NORMAL_MAP = n;
	NORMAL_MAP_DEPTH = 1.0;
}
