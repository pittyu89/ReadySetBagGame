using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// One container on the Journal's left page. JournalPanel fills it with an item and
/// drives its lock/unlock and selection visuals.
/// </summary>
public class JournalSlot : MonoBehaviour
{
    [SerializeField] private Button button;
    [SerializeField] private Image containerImage;
    [SerializeField] private Image iconImage;
    [SerializeField] private Image selectionImage;

    public Button Button => button;
    public Image ContainerImage => containerImage;
    public Image IconImage => iconImage;
    public Image SelectionImage => selectionImage;
}
