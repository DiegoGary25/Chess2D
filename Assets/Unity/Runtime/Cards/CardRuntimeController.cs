using System;
using System.Collections.Generic;
using ChessPrototype.Unity.Data;
using UnityEngine;

namespace ChessPrototype.Unity.Cards
{
    public interface ICardPlaySink
    {
        void ApplyCard(CardDefinition card);
    }

    public sealed class CardRuntimeController : MonoBehaviour
    {
        private readonly List<CardDefinition> _draw = new List<CardDefinition>();
        private readonly List<CardDefinition> _discard = new List<CardDefinition>();
        private readonly List<CardDefinition> _hand = new List<CardDefinition>();
        private System.Random _rng = new System.Random();
        private GameConfigDefinition _cfg;
        public IReadOnlyList<CardDefinition> Hand => _hand;
        public event Action OnHandChanged;

        public void Configure(GameConfigDefinition cfg, int seed)
        {
            _cfg = cfg;
            _rng = new System.Random(seed);
            _draw.Clear(); _discard.Clear(); _hand.Clear();
            if (_cfg != null) _draw.AddRange(_cfg.starterDeck);
            Shuffle(_draw);
        }

        public void DrawFreshTurnHand()
        {
            if (_cfg == null) return;
            while (_hand.Count < _cfg.handSize)
            {
                if (_draw.Count == 0)
                {
                    if (_discard.Count == 0) break;
                    _draw.AddRange(_discard);
                    _discard.Clear();
                    Shuffle(_draw);
                }
                var top = _draw[_draw.Count - 1];
                _draw.RemoveAt(_draw.Count - 1);
                _hand.Add(top);
            }
            OnHandChanged?.Invoke();
        }

        public void DiscardHand()
        {
            _discard.AddRange(_hand);
            _hand.Clear();
            OnHandChanged?.Invoke();
        }

        public bool TryPlayCard(CardDefinition card, ICardPlaySink sink)
        {
            var idx = _hand.IndexOf(card);
            if (idx < 0 || sink == null) return false;
            _hand.RemoveAt(idx);
            _discard.Add(card);
            sink.ApplyCard(card);
            OnHandChanged?.Invoke();
            return true;
        }

        private void Shuffle(List<CardDefinition> items)
        {
            for (var i = items.Count - 1; i > 0; i--)
            {
                var j = _rng.Next(0, i + 1);
                (items[i], items[j]) = (items[j], items[i]);
            }
        }
    }
}

