using ChessPrototype.Unity.Data;
using UnityEngine;
using UnityEngine.UI;

namespace ChessPrototype.Unity.UI
{
    public sealed class UnitFrameAnimator : MonoBehaviour
    {
        private enum AnimState { Idle, Moving, Attack, Action, Hit, Sleep }

        private Image _image;
        private UnitAnimationDefinition _animations;
        private Sprite _staticIcon;
        private AnimState _baseState = AnimState.Idle;
        private AnimState? _oneShotState;
        private int _frameIndex;
        private float _frameTimer;

        public bool HasAnyFrames =>
            HasFrames(_animations != null ? _animations.idleFrames : null) ||
            HasFrames(_animations != null ? _animations.movingFrames : null) ||
            HasFrames(_animations != null ? _animations.attackFrames : null) ||
            HasFrames(_animations != null ? _animations.actionFrames : null) ||
            HasFrames(_animations != null ? _animations.hitFrames : null) ||
            HasFrames(_animations != null ? _animations.sleepFrames : null);

        public void Configure(Image image, UnitAnimationDefinition animations, Sprite staticIcon)
        {
            _image = image;
            _animations = animations;
            _staticIcon = staticIcon;
            _oneShotState = null;
            _baseState = AnimState.Idle;
            ResetPlayback();
            ApplyCurrentVisual();
        }

        public void SetSleeping(bool sleeping)
        {
            var next = sleeping ? AnimState.Sleep : AnimState.Idle;
            if (_baseState == next) return;

            _baseState = next;
            if (_oneShotState == null)
            {
                ResetPlayback();
                ApplyCurrentVisual();
            }
        }

        public void PlayMoveOneShot()
        {
            StartOneShot(AnimState.Moving);
        }

        public void PlayAttackOneShot()
        {
            StartOneShot(AnimState.Attack);
        }

        public void PlayActionOneShot()
        {
            StartOneShot(AnimState.Action);
        }

        public void PlayHitOneShot()
        {
            StartOneShot(AnimState.Hit);
        }

        private void Update()
        {
            if (_image == null) return;
            TickAnimation(Time.deltaTime);
        }

        private void StartOneShot(AnimState state)
        {
            if (_image == null) return;
            _oneShotState = state;
            ResetPlayback();
            ApplyCurrentVisual();
        }

        private void TickAnimation(float deltaTime)
        {
            var state = _oneShotState ?? _baseState;
            var frames = FramesFor(state);
            var fps = FpsFor(state);

            if (!HasFrames(frames) || fps <= 0f)
            {
                if (_oneShotState != null)
                {
                    _oneShotState = null;
                    ResetPlayback();
                    ApplyCurrentVisual();
                }
                return;
            }

            _frameTimer += deltaTime;
            var step = 1f / fps;
            if (_frameTimer < step) return;

            while (_frameTimer >= step)
            {
                _frameTimer -= step;
                _frameIndex += 1;
            }

            if (_frameIndex >= frames.Length)
            {
                if (_oneShotState != null)
                {
                    _oneShotState = null;
                    ResetPlayback();
                    ApplyCurrentVisual();
                    return;
                }
                _frameIndex %= frames.Length;
            }

            var sprite = frames[_frameIndex];
            if (sprite != null) _image.sprite = sprite;
        }

        private void ApplyCurrentVisual()
        {
            if (_image == null) return;
            var state = _oneShotState ?? _baseState;
            var frames = FramesFor(state);

            if (HasFrames(frames))
            {
                _image.sprite = frames[0];
                return;
            }

            if (_staticIcon != null) _image.sprite = _staticIcon;
        }

        private void ResetPlayback()
        {
            _frameIndex = 0;
            _frameTimer = 0f;
        }

        private Sprite[] FramesFor(AnimState state)
        {
            if (_animations == null) return null;
            switch (state)
            {
                case AnimState.Moving: return _animations.movingFrames;
                case AnimState.Attack: return _animations.attackFrames;
                case AnimState.Action: return _animations.actionFrames;
                case AnimState.Hit: return _animations.hitFrames;
                case AnimState.Sleep: return _animations.sleepFrames;
                default: return _animations.idleFrames;
            }
        }

        private float FpsFor(AnimState state)
        {
            if (_animations == null) return 0f;
            switch (state)
            {
                case AnimState.Moving: return _animations.movingFps;
                case AnimState.Attack: return _animations.attackFps;
                case AnimState.Action: return _animations.actionFps;
                case AnimState.Hit: return _animations.hitFps;
                case AnimState.Sleep: return _animations.sleepFps;
                default: return _animations.idleFps;
            }
        }

        private static bool HasFrames(Sprite[] frames)
        {
            return frames != null && frames.Length > 0;
        }
    }
}
