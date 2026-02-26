using System;
using UnityEngine;

namespace ChessPrototype.Unity.Data
{
    [Serializable]
    public sealed class UnitAnimationDefinition
    {
        [Header("Looping")]
        public Sprite[] idleFrames;
        [Min(0f)] public float idleFps = 2f;

        [Header("One-shot")]
        public Sprite[] movingFrames;
        [Min(0f)] public float movingFps = 2f;
        public Sprite[] attackFrames;
        [Min(0f)] public float attackFps = 2f;
        public Sprite[] actionFrames;
        [Min(0f)] public float actionFps = 2f;
        public Sprite[] hitFrames;
        [Min(0f)] public float hitFps = 2f;

        [Header("Looping")]
        public Sprite[] sleepFrames;
        [Min(0f)] public float sleepFps = 2f;
    }
}
