using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace VolumeRendering.Runtime
{
    // U Play modu sve skripte rade s jednom instancom materijala volumena umjesto sa shared assetom
    // (Volume Rendering.mat). Inače se sve što se postavi u Playu (rez, način prikaza, teksture)
    // zapisuje u asset: vrijednosti preživljavaju Play, .mat je stalno izmijenjen u gitu, a _Volume
    // nakon Playa pokazuje na uništenu teksturu.
    //
    // Instanca nastaje pri prvom pozivu, bez obzira na to koja skripta je prva, i postavlja se na sve
    // renderere u sceni koji koriste taj asset. Zato i stare skripte koje čitaju renderer.sharedMaterial
    // dobiju istu instancu. Izvan Play moda (Editor, ExecuteInEditMode) vraća se asset, kao prije.
    public static class RuntimeMaterial
    {
        // asset → instanca
        static readonly Dictionary<Material, Material> _instances = new Dictionary<Material, Material>();

        // Za [SerializeField] Material polja koja pokazuju na asset. Pozvati u Awake.
        public static Material Resolve(Material material)
        {
            if (material == null || !Application.isPlaying)
                return material;

            if (_instances.TryGetValue(material, out var instance) && instance != null)
                return instance;
            if (_instances.ContainsValue(material))
                return material;   // već je instanca

            instance = new Material(material) { name = material.name + " (Runtime)" };
            _instances[material] = instance;
            AssignToRenderers(material, instance);
            return instance;
        }

        // Za skripte na samom objektu volumena, koje materijal uzimaju s renderera.
        public static Material For(Renderer renderer) =>
            renderer != null ? Resolve(renderer.sharedMaterial) : null;

        static void AssignToRenderers(Material asset, Material instance)
        {
            foreach (var renderer in Object.FindObjectsOfType<Renderer>())
            {
                if (renderer.sharedMaterial == asset)
                    renderer.sharedMaterial = instance;
            }
        }

        // Novoučitana scena ima renderere s assetom; postavi im postojeće instance.
        static void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            foreach (var pair in _instances)
            {
                if (pair.Value != null)
                    AssignToRenderers(pair.Key, pair.Value);
            }
        }

        static void DestroyInstances()
        {
            foreach (var instance in _instances.Values)
            {
                if (instance != null)
                    Object.Destroy(instance);
            }
            _instances.Clear();
        }

        // Statičko stanje se ne smije prenijeti u idući Play (ni uz isključen domain reload).
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetState()
        {
            _instances.Clear();
            SceneManager.sceneLoaded -= HandleSceneLoaded;
            SceneManager.sceneLoaded += HandleSceneLoaded;
            Application.quitting -= DestroyInstances;
            Application.quitting += DestroyInstances;
        }
    }
}
