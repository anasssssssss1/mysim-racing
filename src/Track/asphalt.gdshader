shader_type spatial;

// World-space asphalt: two detail scales (no visible tiling), large-scale patching,
// and a rubbered darker band near the middle of the road (UV.y = 0..1 across).
uniform sampler2D detail : hint_default_white, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D detail_n : hint_normal, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D macro : hint_default_white, filter_linear_mipmap, repeat_enable;
uniform vec3 base_color : source_color = vec3(0.13, 0.13, 0.135);
uniform float rubber = 0.25;

varying vec3 wpos;

void vertex() {
	wpos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
}

void fragment() {
	vec2 p = wpos.xz;
	float fine = texture(detail, p * 1.9).r;
	float mid = texture(detail, p * 0.23 + vec2(0.37, 0.11)).r;
	float big = texture(macro, p * 0.004).r;
	float band = 1.0 - smoothstep(0.08, 0.32, abs(UV.y - 0.5));
	vec3 c = base_color * mix(0.7, 1.25, fine) * mix(0.85, 1.12, mid) * mix(0.88, 1.1, big);
	c *= 1.0 - rubber * band;
	ALBEDO = c;
	ROUGHNESS = clamp(0.92 - 0.18 * band - 0.08 * big, 0.0, 1.0);
	SPECULAR = 0.35;
	NORMAL_MAP = texture(detail_n, p * 1.9).rgb;
	NORMAL_MAP_DEPTH = 0.5;
}
