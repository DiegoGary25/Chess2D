using UnityEngine;
using UnityEngine.UI;

namespace ChessPrototype.Unity.UI
{
    public sealed class SegmentedBarView : MonoBehaviour
    {
        [SerializeField] private RectTransform segmentsRoot;

        public void SetValue(int current, int max, Color activeTint)
        {
            var root = segmentsRoot != null ? segmentsRoot : transform as RectTransform;
            if (root == null) return;

            var clampedMax = Mathf.Clamp(max, 0, root.childCount);
            var clampedCurrent = Mathf.Clamp(current, 0, clampedMax);

            for (var i = 0; i < root.childCount; i++)
            {
                var child = root.GetChild(i);
                var shouldBeOn = i < clampedCurrent;
                var inRange = i < clampedMax;
                child.gameObject.SetActive(inRange && shouldBeOn);

                if (!inRange || !shouldBeOn) continue;
                var graphic = child.GetComponent<Graphic>();
                if (graphic != null) graphic.color = activeTint;
            }
        }
    }
}
