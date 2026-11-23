shader_type spatial;

// City blocks: instance colour for the facade, window grid from world position (3.5 m floors).
varying vec3 wpos;
varying vec3 wnorm;
varying vec3 col;

void vertex() {
	wpos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
	wnorm = normalize((MODEL_MATRIX * vec4(NORMAL, 0.0)).xyz);
	col = COLOR.rgb;
}

void fragment() {
	bool wall = abs(wnorm.y) < 0.5;
	float along = abs(wnorm.x) > 0.5 ? wpos.z : wpos.x;
	float fx = fract(along / 2.8);
	float fy = fract(wpos.y / 3.5);
	float win = wall && wpos.y > 1.0 ? step(0.16, fx) * step(fx, 0.84) * step(0.22, fy) * step(fy, 0.86) : 0.0;
	float lit = step(0.7, fract(sin(dot(floor(vec2(along / 2.8, wpos.y / 3.5)), vec2(12.9898, 78.233))) * 43758.5453));
	vec3 glass = mix(vec3(0.06, 0.09, 0.12), vec3(0.16, 0.2, 0.24), lit);
	ALBEDO = mix(col, glass, win);
	ROUGHNESS = mix(0.85, 0.06, win);
	METALLIC = mix(0.0, 0.7, win);
}
