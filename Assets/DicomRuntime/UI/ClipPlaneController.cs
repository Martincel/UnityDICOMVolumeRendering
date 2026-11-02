using UnityEngine;

namespace VolumeRendering.Runtime.UI
{
    // Kosa ravnina reza: ravnina je zadana transformom ovog objekta (npr. Quad). Svaki okvir se točka
    // objekta i njegova normala pretvaraju u lokalni prostor volumena i šalju shaderu (_ClipPlane).
    // Vidljiva ostaje strana prema kojoj pokazuje transform.forward (flip je okreće).
    //
    // Točka ide kroz worldToLocalMatrix, a normala kroz transponiranu localToWorldMatrix: ravnina je
    // kovektor, pa to jedino daje točan rez i uz neuniformnu skalu volumena.
    // Dok je komponenta isključena, rez je isključen. U Playu je materijal instanca (RuntimeMaterial).
    // U 3D sceni se ravnina pomiče u Editoru; u VR sceni je postavlja VrMenu (poza desnog kontrolera).
    // Pravo hvatanje rukom dolazi s XRI.
    public class ClipPlaneController : MonoBehaviour
    {
        [SerializeField] Transform volume;
        [SerializeField] Material volumeMaterial;
        [SerializeField] bool flip;

        static readonly int ClipPlaneId = Shader.PropertyToID("_ClipPlane");
        static readonly int ClipPlaneOnId = Shader.PropertyToID("_ClipPlaneOn");

        void Awake() => volumeMaterial = RuntimeMaterial.Resolve(volumeMaterial);

        // Za komponentu dodanu u runtimeu (VR izbornik), kad polja nisu postavljena u sceni.
        public void Bind(Transform volumeTransform, Material material)
        {
            volume = volumeTransform;
            volumeMaterial = RuntimeMaterial.Resolve(material);
        }

        void OnDisable()
        {
            if (volumeMaterial != null)
                volumeMaterial.SetFloat(ClipPlaneOnId, 0f);
        }

        void LateUpdate()
        {
            if (volume == null || volumeMaterial == null)
                return;

            Vector3 normalWorld = flip ? -transform.forward : transform.forward;
            Vector3 normalLocal = volume.localToWorldMatrix.transpose.MultiplyVector(normalWorld);
            if (normalLocal.sqrMagnitude < 1e-12f)
                return;
            normalLocal.Normalize();

            Vector3 pointLocal = volume.worldToLocalMatrix.MultiplyPoint3x4(transform.position);

            volumeMaterial.SetVector(ClipPlaneId, new Vector4(
                normalLocal.x, normalLocal.y, normalLocal.z, -Vector3.Dot(normalLocal, pointLocal)));
            volumeMaterial.SetFloat(ClipPlaneOnId, 1f);
        }
    }
}
