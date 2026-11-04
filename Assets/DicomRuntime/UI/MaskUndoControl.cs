using UnityEngine;
using UnityEngine.UI;
using VolumeRendering.Runtime.Annotation;

namespace VolumeRendering.Runtime.UI
{
    // Poništavanje oznaka: gumb "Poništi" (opcionalan) i Ctrl+Z. Zakači na bilo koji GameObject u sceni i u
    // Inspectoru postavi manager (i gumb, ako ga ima). Gumb je neaktivan kad nema što poništiti.
    public class MaskUndoControl : MonoBehaviour
    {
        [SerializeField] AnnotationMaskManager maskManager;
        [SerializeField] Button undoButton;

        void OnEnable()
        {
            if (undoButton != null) undoButton.onClick.AddListener(Undo);
        }

        void OnDisable()
        {
            if (undoButton != null) undoButton.onClick.RemoveListener(Undo);
        }

        void Update()
        {
            if (maskManager == null)
                return;

            if (undoButton != null)
                undoButton.interactable = maskManager.CanUndo;

            bool ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
            if (ctrl && Input.GetKeyDown(KeyCode.Z))
                Undo();
        }

        void Undo()
        {
            if (maskManager != null)
                maskManager.Undo();
        }
    }
}
