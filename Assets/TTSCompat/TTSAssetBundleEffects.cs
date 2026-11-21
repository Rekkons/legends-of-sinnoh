// TTS 피규어 번들의 효과표 대역 클래스(03 §2-5 파이프라인 3, d3-1 §5-3 ⑤). 들여오기(Sinnoh.Editor.FigureImport) 때만 쓰인다.
// 번들의 MonoScript가 Assembly-CSharp / 네임스페이스 없음 / TTSAssetBundleEffects를 가리키므로 asmdef 없이 이 폴더에 두고 네임스페이스를 붙이지 않는다.
// 필드 이름은 binary2text로 본 타입 트리와 같다(이름이 맞아 그대로 읽힌다). 프리팹에는 저장하지 않는다(번들 MonoScript를 가리킴) — 표는 Gen/Figures/figures.json으로 뺀다.
using System;
using System.Collections.Generic;
using UnityEngine;

public class TTSAssetBundleEffects : MonoBehaviour
{
    public List<TTSLoopingEffect> LoopingEffects = new List<TTSLoopingEffect>();
    public List<TTSTriggerEffect> TriggerEffects = new List<TTSTriggerEffect>();
}

[Serializable] public class TTSGameObject { }
[Serializable] public class TTSParticle { }

[Serializable]
public class TTSSound
{
    public AudioClip Audio;
    public bool Positional3D;
}

[Serializable]
public class TTSAnimation
{
    public Animation AnimationComponent;
    public string AnimationName;
}

[Serializable]
public class TTSAnimator
{
    public Animator AnimatorComponent;
    public string StateName;
}

[Serializable]
public class TTSLoopingEffect
{
    public string Name;
    public List<TTSGameObject> GameObjects;
    public List<TTSParticle> Particles;
    public TTSSound Sound;
    public TTSAnimation Animation;
    public TTSAnimator Animator;
}

[Serializable]
public class TTSTriggerEffect
{
    public string Name;
    public List<TTSGameObject> GameObjects;
    public List<TTSParticle> Particles;
    public TTSSound Sound;
    public TTSAnimation Animation;
    public TTSAnimator Animator;
    public float Duration;
}
