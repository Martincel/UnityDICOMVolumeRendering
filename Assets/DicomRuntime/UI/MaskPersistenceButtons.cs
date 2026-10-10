using System;
using System.IO;
using UnityEngine;
using UnityEngine.UI;
using VolumeRendering.Runtime.Annotation;

namespace VolumeRendering.Runtime.UI
{
    // Gumbi "Spremi" / "Učitaj" za anotacijsku masku. Samo zakači na bilo koji GameObject i u
    // Inspectoru postavi reference (manager + dva gumba; statusText je opcionalan).
    // Datoteka: Application.persistentDataPath/<fileName>.
    public class MaskPersistenceButtons : MonoBehaviour
    {
        [SerializeField] AnnotationMaskManager maskManager;
        [SerializeField] Button saveButton;
        [SerializeField] Button loadButton;
        [SerializeField] Text statusText; // opcionalno: prikaz rezultata ili greške
        [SerializeField] string fileName = "mask.dmask";

        string FilePath => Path.Combine(Application.persistentDataPath, fileName);

        void OnEnable()
        {
            if (saveButton != null) saveButton.onClick.AddListener(Save);
            if (loadButton != null) loadButton.onClick.AddListener(Load);
        }

        void OnDisable()
        {
            if (saveButton != null) saveButton.onClick.RemoveListener(Save);
            if (loadButton != null) loadButton.onClick.RemoveListener(Load);
        }

        void Save() => Run("Spremljeno", () => maskManager.SaveToFile(FilePath));
        void Load() => Run("Učitano", () => maskManager.LoadFromFile(FilePath));

        void Run(string successMessage, Action action)
        {
            if (maskManager == null)
            {
                ShowStatus("Nedostaje referenca na AnnotationMaskManager.", true);
                return;
            }

            try
            {
                action();
                ShowStatus($"{successMessage}: {FilePath}", false);
            }
            catch (Exception e)
            {
                ShowStatus(e.Message, true);
            }
        }

        void ShowStatus(string message, bool isError)
        {
            if (isError) Debug.LogWarning("[Maska] " + message);
            else Debug.Log("[Maska] " + message);

            if (statusText != null)
                statusText.text = message;
        }
    }
}
