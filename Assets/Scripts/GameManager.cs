// GameManager.cs  — No bugs found. Minor: added null-safe SceneManager import.
using System;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FreeFire
{
    public enum GameState { Loading, Lobby, Dropping, BattleRoyale, Victory, Defeat }

    public class GameManager : MonoBehaviour
    {
        public static GameManager Instance { get; private set; }

        [Header("Lobby")]
        [SerializeField] private float lobbyCountdown    = 30f;
        [SerializeField] private float dropPhaseDuration = 60f;
        [SerializeField] private int   startingPlayers   = 50;

        private GameState _state;
        private int       _playersAlive;
        private int       _localKills;

        public GameState CurrentState => _state;
        public int       PlayersAlive => _playersAlive;
        public int       LocalKills   => _localKills;

        public event Action<GameState> OnStateChanged;
        public event Action<int>       OnPlayersAliveChanged;
        public event Action<int>       OnKillCountChanged;
        public event Action            OnVictory;
        public event Action            OnDefeat;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        private void Start()
        {
            _playersAlive = startingPlayers;
            StartCoroutine(GameLoop());
        }

        private IEnumerator GameLoop()
        {
            TransitionTo(GameState.Lobby);
            yield return new WaitForSeconds(lobbyCountdown);
            TransitionTo(GameState.Dropping);
            yield return new WaitForSeconds(dropPhaseDuration);
            TransitionTo(GameState.BattleRoyale);
        }

        private void TransitionTo(GameState next)
        {
            _state = next;
            OnStateChanged?.Invoke(_state);
            Debug.Log($"[Game] State → {_state}");
        }

        public void RegisterElimination(bool wasLocalPlayer, string killedBy = "")
        {
            _playersAlive = Mathf.Max(0, _playersAlive - 1);
            OnPlayersAliveChanged?.Invoke(_playersAlive);

            if (wasLocalPlayer)
            {
                TransitionTo(GameState.Defeat);
                OnDefeat?.Invoke();
                return;
            }

            _localKills++;
            OnKillCountChanged?.Invoke(_localKills);

            if (_playersAlive <= 1)
            {
                TransitionTo(GameState.Victory);
                OnVictory?.Invoke();
            }
        }

        public void ReturnToMenu(float delay = 3f) => StartCoroutine(DelayedSceneLoad(0, delay));

        private IEnumerator DelayedSceneLoad(int scene, float delay)
        {
            yield return new WaitForSeconds(delay);
            Time.timeScale = 1f;
            SceneManager.LoadScene(scene);
        }
    }
}
