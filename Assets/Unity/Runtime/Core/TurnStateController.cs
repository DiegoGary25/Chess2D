using System;
using ChessPrototype.Unity.Data;
using UnityEngine;

namespace ChessPrototype.Unity.Core
{
    public sealed class TurnStateController : MonoBehaviour
    {
        [SerializeField] private TurnPhase phase = TurnPhase.Player;
        [SerializeField] private int round = 1;
        [SerializeField] private int playerEnergy = 3;
        [SerializeField] private int energyPerRound = 3;

        public TurnPhase Phase => phase;
        public int Round => round;
        public int PlayerEnergy => playerEnergy;
        public event Action OnPhaseChanged;
        public event Action OnEnergyChanged;

        public void Configure(int energyPerRoundValue)
        {
            energyPerRound = Mathf.Max(1, energyPerRoundValue);
        }

        public void BeginEncounter()
        {
            round = 1;
            phase = TurnPhase.Player;
            playerEnergy = energyPerRound;
            OnPhaseChanged?.Invoke();
            OnEnergyChanged?.Invoke();
        }

        public bool SpendEnergy(int amount)
        {
            if (phase != TurnPhase.Player || amount < 0 || amount > playerEnergy) return false;
            playerEnergy -= amount;
            OnEnergyChanged?.Invoke();
            return true;
        }

        public void EndPlayerTurn()
        {
            phase = TurnPhase.Enemy;
            OnPhaseChanged?.Invoke();
        }

        public void EndEnemyTurn()
        {
            phase = TurnPhase.Player;
            round += 1;
            playerEnergy = energyPerRound;
            OnPhaseChanged?.Invoke();
            OnEnergyChanged?.Invoke();
        }
    }
}

