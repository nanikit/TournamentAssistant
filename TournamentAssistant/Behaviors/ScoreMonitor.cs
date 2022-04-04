using BS_Utils.Utilities;
using System;
using System.Collections;
using System.Linq;
using TournamentAssistant.UI.FlowCoordinators;
using TournamentAssistant.Utilities;
using TournamentAssistantShared;
using TournamentAssistantShared.Models;
using TournamentAssistantShared.Models.Packets;
using UnityEngine;

namespace TournamentAssistant.Behaviors
{
    class ScoreMonitor : MonoBehaviour
    {
        public static ScoreMonitor Instance { get; set; }

        private ScoreController _scoreController;
        private AudioTimeSyncController _audioTimeSyncController;

        private Guid[] destinationPlayers;

        private int _lastUpdateScore = 0;
        private int _lastCombo = 0;
        private int _scoreUpdateFrequency = Plugin.client.State.ServerSettings.ScoreUpdateFrequency;
        private int _scoreCheckDelay = 0;
        private int _notesMissed = 0;
        private int _lastUpdateNotesMissed = 0; //Notes missed as of last time an update was sent to the server

        void Awake()
        {
            Instance = this;

            DontDestroyOnLoad(this); //Will actually be destroyed when the main game scene is loaded again, but unfortunately this 
                                     //object is created before the game scene loads, so we need to do this to prevent the game scene
                                     //load from destroying it

            StartCoroutine(WaitForComponentCreation());
        }

        private void ScoreUpdated(int score, int combo, float accuracy, float time, int notesMissed)
        {
            //Send score update
            (Plugin.client.Self as Player).Score = score;
            (Plugin.client.Self as Player).Combo = combo;
            (Plugin.client.Self as Player).Accuracy = accuracy;
            (Plugin.client.Self as Player).SongPosition = time;
            (Plugin.client.Self as Player).Misses = notesMissed;
            var playerUpdate = new Event();
            playerUpdate.Type = Event.EventType.PlayerUpdated;
            playerUpdate.ChangedObject = Plugin.client.Self;

            //NOTE:/TODO: We don't needa be blasting the entire server
            //with score updates. This update will only go out to other
            //players in the current match and the coordinator
            Plugin.client.Send(destinationPlayers, new Packet(playerUpdate));
        }

        public IEnumerator WaitForComponentCreation()
        {
            var coordinator = Resources.FindObjectsOfTypeAll<RoomCoordinator>().FirstOrDefault();
            var match = coordinator?.Match;
            destinationPlayers = ((bool)(coordinator?.TournamentMode) && !Plugin.UseFloatingScoreboard) ?
                new Guid[] { match.Leader.Id } :
                match.Players.Select(x => x.Id).Union(new Guid[] { match.Leader.Id }).ToArray(); //We don't wanna be doing this every frame
                                                                                                 //new string[] { "x_x" }; //Note to future moon, this will cause the server to receive the forwarding packet and forward it to no one. Since it's received, though, the scoreboard will get it if connected

            yield return new WaitUntil(() => Resources.FindObjectsOfTypeAll<ScoreController>().Any());
            yield return new WaitUntil(() => Resources.FindObjectsOfTypeAll<AudioTimeSyncController>().Any());
            _scoreController = Resources.FindObjectsOfTypeAll<ScoreController>().First();
            _audioTimeSyncController = Resources.FindObjectsOfTypeAll<AudioTimeSyncController>().First();

            _scoreController.scoringForNoteFinishedEvent += HandleNote;
            BSEvents.comboDidChange += ReflectBsComboDidChange;
            _scoreController.scoreDidChangeEvent += ReflectScore;
        }

        private void ReflectScore(int multipliedScore, int modifiedScore)
        {
            _lastUpdateScore = modifiedScore;
            ScoreUpdated(_scoreController.modifiedScore, _lastCombo, (float)_scoreController.multipliedScore / _scoreController.immediateMaxPossibleMultipliedScore, _audioTimeSyncController.songTime, _notesMissed);
        }

        private void ReflectBsComboDidChange(int combo)
        {
            _lastCombo = combo;
            ScoreUpdated(_scoreController.modifiedScore, _lastCombo, (float)_scoreController.multipliedScore / _scoreController.immediateMaxPossibleMultipliedScore, _audioTimeSyncController.songTime, _notesMissed);
        }

        private void HandleNote(ScoringElement scoring)
        {
            if (scoring.cutScore == 0)
            {
                _notesMissed++;
            }
        }

        public void HandleNoteMissed(NoteData data, int something)
        {
            if (data.colorType != ColorType.None)
            {
                _notesMissed++;
            }
        }

        public void HandleNoteCut(NoteData data, in NoteCutInfo info, int multipler)
        {
            if (!info.allIsOK && data.colorType != ColorType.None)
            {
                _notesMissed++;
            }
        }

        public static void Destroy() => Destroy(Instance);

        void OnDestroy()
        {
            _scoreController.scoringForNoteFinishedEvent -= HandleNote;
            Instance = null;
        }
    }
}
