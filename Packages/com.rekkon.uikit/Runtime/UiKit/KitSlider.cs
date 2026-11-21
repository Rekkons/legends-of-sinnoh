using System;
using UnityEngine.UIElements;

namespace Rekkon.UiKit
{
    /// <summary>
    /// 슬라이더(값 = 0 ~ <see cref="Max"/>의 정수 단계): 홈 + 채운 부분 + 손잡이. 홈을 누르면 그 자리의 단계로 가고, 누른 채 끌면 따라온다(끄는 동안 포인터를 잡는다).
    /// 엔진의 Slider를 쓰지 않는다 — 기본 테마의 모양·dragger 구조를 받지 않고 도트 토큰(<c>kit-slider*</c>)으로만 그린다. 자리 셈은 <see cref="SliderMath"/>(정수 px).
    /// 값이 바뀔 때만 <see cref="Changed"/>가 불린다(사람이 움직였을 때와 <see cref="Set"/> — <see cref="Value"/>에 넣는 것은 조용하다). 꺼짐은 <c>SetEnabled(false)</c>.
    /// 요소 이름: 채운 부분 <c>kit-slider-fill</c>, 손잡이 <c>kit-slider-knob</c>(클래스와 같다).
    /// </summary>
    public sealed class KitSlider : VisualElement
    {
        readonly VisualElement fill, knob;
        int value;
        bool dragging;

        public int Max { get; }

        /// <summary>값이 바뀌었다(새 값).</summary>
        public Action<int> Changed;

        public KitSlider(int max, int value = 0, Action<int> changed = null)
        {
            Max = Math.Max(1, max);
            this.value = SliderMath.Clamp(value, Max);
            Changed = changed;
            AddToClassList("kit-slider");
            var track = new VisualElement { pickingMode = PickingMode.Ignore };
            track.AddToClassList("kit-slider-track");
            Add(track);
            fill = new VisualElement { name = "kit-slider-fill", pickingMode = PickingMode.Ignore };
            fill.AddToClassList("kit-slider-fill");
            Add(fill);
            knob = new VisualElement { name = "kit-slider-knob", pickingMode = PickingMode.Ignore };
            knob.AddToClassList("kit-slider-knob");
            Add(knob);
            RegisterCallback<GeometryChangedEvent>(e => Place());
            knob.RegisterCallback<GeometryChangedEvent>(e => Place());
            RegisterCallback<PointerDownEvent>(OnDown);
            RegisterCallback<PointerMoveEvent>(OnMove);
            RegisterCallback<PointerUpEvent>(OnUp);
            RegisterCallback<PointerCaptureOutEvent>(e => dragging = false);
        }

        /// <summary>지금 값. 넣으면 손잡이만 옮긴다(<see cref="Changed"/>는 부르지 않는다).</summary>
        public int Value
        {
            get => value;
            set
            {
                this.value = SliderMath.Clamp(value, Max);
                Place();
            }
        }

        /// <summary>값을 고른다(사람이 움직인 것과 같다): 바뀌었으면 <see cref="Changed"/>를 부른다. 꺼져 있으면 아무 일도 없다.</summary>
        public void Set(int next)
        {
            next = SliderMath.Clamp(next, Max);
            if (!enabledInHierarchy || next == value) return;
            value = next;
            Place();
            Changed?.Invoke(value);
        }

        /// <summary>홈 안의 자리 x(이 요소의 왼쪽 끝이 0)에 해당하는 값.</summary>
        public int ValueAt(float x) => SliderMath.ValueAt(x, contentRect.width, knob.resolvedStyle.width, Max);

        void OnDown(PointerDownEvent e)
        {
            if (e.button != 0 || !enabledInHierarchy) return;
            dragging = true;
            this.CapturePointer(e.pointerId);
            Set(ValueAt(e.localPosition.x));
            e.StopPropagation();
        }

        void OnMove(PointerMoveEvent e)
        {
            if (dragging && this.HasPointerCapture(e.pointerId)) Set(ValueAt(e.localPosition.x));
        }

        void OnUp(PointerUpEvent e)
        {
            if (!dragging) return;
            dragging = false;
            if (this.HasPointerCapture(e.pointerId)) this.ReleasePointer(e.pointerId);
        }

        // 손잡이·채운 부분을 지금 값의 자리에(배치가 서기 전에는 폭이 NaN — 0에 둔다)
        void Place()
        {
            float knobWidth = knob.resolvedStyle.width;
            int left = SliderMath.KnobLeft(value, contentRect.width, knobWidth, Max);
            knob.style.left = left;
            fill.style.width = float.IsNaN(knobWidth) ? 0f : left + knobWidth * 0.5f;
        }
    }
}
