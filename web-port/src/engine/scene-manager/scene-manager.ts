import * as THREE from "three";

export class SceneManager {
  public readonly scene: THREE.Scene;
  public readonly camera: THREE.PerspectiveCamera;

  private readonly gradientSphere: THREE.Mesh;
  private readonly particles: THREE.Points;

  public constructor() {
    this.scene = new THREE.Scene();
    this.camera = new THREE.PerspectiveCamera(44, window.innerWidth / window.innerHeight, 0.1, 2000);
    this.camera.position.set(0, 0, 18);

    const hemisphere = new THREE.HemisphereLight(0xbfc4c6, 0x2f3538, 1.2);
    this.scene.add(hemisphere);

    const directional = new THREE.DirectionalLight(0x89b49e, 0.75);
    directional.position.set(4, 6, 10);
    this.scene.add(directional);

    const sphereGeo = new THREE.SphereGeometry(48, 48, 48);
    const sphereMat = new THREE.ShaderMaterial({
      side: THREE.BackSide,
      uniforms: {
        topColor: { value: new THREE.Color("#3f4447") },
        midColor: { value: new THREE.Color("#8b9093") },
        bottomColor: { value: new THREE.Color("#f4f5f5") },
      },
      vertexShader: `
        varying vec3 vWorldPosition;
        void main() {
          vec4 worldPosition = modelMatrix * vec4(position, 1.0);
          vWorldPosition = worldPosition.xyz;
          gl_Position = projectionMatrix * modelViewMatrix * vec4(position, 1.0);
        }
      `,
      fragmentShader: `
        varying vec3 vWorldPosition;
        uniform vec3 topColor;
        uniform vec3 midColor;
        uniform vec3 bottomColor;

        void main() {
          float h = normalize(vWorldPosition).y * 0.5 + 0.5;
          vec3 c1 = mix(bottomColor, midColor, smoothstep(0.15, 0.58, h));
          vec3 c2 = mix(midColor, topColor, smoothstep(0.58, 1.0, h));
          vec3 c = mix(c1, c2, smoothstep(0.52, 1.0, h));
          gl_FragColor = vec4(c, 1.0);
        }
      `,
    });

    this.gradientSphere = new THREE.Mesh(sphereGeo, sphereMat);
    this.scene.add(this.gradientSphere);

    const particleGeo = new THREE.BufferGeometry();
    const points = new Float32Array(300 * 3);
    for (let i = 0; i < points.length; i += 3) {
      points[i] = (Math.random() - 0.5) * 52;
      points[i + 1] = (Math.random() - 0.5) * 26;
      points[i + 2] = -Math.random() * 45;
    }
    particleGeo.setAttribute("position", new THREE.BufferAttribute(points, 3));

    const particleMat = new THREE.PointsMaterial({
      color: 0xf4f7f6,
      transparent: true,
      opacity: 0.16,
      size: 0.08,
      sizeAttenuation: true,
      depthWrite: false,
    });

    this.particles = new THREE.Points(particleGeo, particleMat);
    this.scene.add(this.particles);
  }

  public update(elapsedSeconds: number): void {
    this.particles.rotation.y = elapsedSeconds * 0.015;
    this.particles.position.y = Math.sin(elapsedSeconds * 0.24) * 0.2;
  }

  public resize(width: number, height: number): void {
    this.camera.aspect = width / height;
    this.camera.updateProjectionMatrix();
  }
}
