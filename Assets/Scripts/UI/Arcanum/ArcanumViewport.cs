using UnityEngine;
using UnityEngine.EventSystems;

namespace Orivilon.UI.Arcanum
{
    /// <summary>Plocha mapy: tažení posouvá, kolečko přibližuje kolem kurzoru, klik mimo uzel zavře připnutý detail.
    /// (Vlastní soubor, aby šla komponenta uložit do prefabu.)</summary>
    public class ArcanumViewport : MonoBehaviour,
        IBeginDragHandler, IDragHandler, IEndDragHandler, IScrollHandler, IPointerClickHandler
    {
        public ArcanumUI owner;

        public void OnBeginDrag(PointerEventData e)
        {
            if (e.button == PointerEventData.InputButton.Left) owner.BeginPan();
        }

        public void OnDrag(PointerEventData e)
        {
            if (e.button == PointerEventData.InputButton.Left) owner.Pan(e.delta);
        }

        public void OnEndDrag(PointerEventData e) => owner.EndPan();

        public void OnScroll(PointerEventData e) => owner.ZoomAt(e.position, e.scrollDelta.y);

        public void OnPointerClick(PointerEventData e)
        {
            if (!e.dragging && e.pointerPress == gameObject) owner.OnBackgroundClick();
        }
    }
}
