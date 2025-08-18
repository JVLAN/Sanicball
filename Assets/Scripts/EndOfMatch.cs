using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Sanicball.Data;
using Sanicball.Logic;
using Sanicball.UI;
using SanicballCore;
using UnityEngine;

namespace Sanicball
{
    public class EndOfMatch : MonoBehaviour
    {
        [SerializeField]
        private Transform[] topPositionSpawnpoints = null;
        [SerializeField]
        private Transform lowerPositionsSpawnpoint = null;
        [SerializeField]
        private Scoreboard scoreboardPrefab = null;
        [SerializeField]
        private Camera cam = null;
        [SerializeField]
        private Rotate camRotate = null;

        private Scoreboard activeScoreboard;
        private bool hasActivatedOnce = false;

        private List<RacePlayer> movedPlayers = new List<RacePlayer>();

        public void Activate(RaceManager manager)
        {
            if (!hasActivatedOnce)
            {
                //Activate with fade
                CameraFade.StartAlphaFade(Color.black, false, 1f, 0, () =>
                {
                    CameraFade.StartAlphaFade(Color.black, true, 1f);
                    ActivateInner(manager);
                });
            }
            else
            {
                //Activate without fade
                ActivateInner(manager);
            }
        }

        private void ActivateInner(RaceManager manager)
        {
            if (!hasActivatedOnce)
            {
                hasActivatedOnce = true;

                activeScoreboard = Instantiate(scoreboardPrefab);

                for (int i = 0; i < manager.PlayerCount; i++)
                {
                    RacePlayer p = manager[i];
                    if (p.Camera != null)
                        p.Camera.Remove();
                }

                foreach (RaceUI ui in FindObjectsOfType<RaceUI>())
                {
                    Destroy(ui.gameObject);
                }

                foreach (PlayerUI ui in FindObjectsOfType<PlayerUI>())
                {
                    Destroy(ui.gameObject);
                }

                cam.gameObject.SetActive(true);
                camRotate.angle = new Vector3(0, 1, 0);
                //cam.enabled = true;
            }
            activeScoreboard.DisplayResults(manager);
            ExportResults(manager);

            List<RacePlayer> movablePlayers = new List<RacePlayer>();
            for (int i = 0; i < manager.PlayerCount; i++)
            {
                RacePlayer rp = manager[i];
                if (rp.RaceFinished && !rp.FinishReport.Disqualified)
                {
                    movablePlayers.Add(rp);
                }
            }
            movablePlayers.Sort((a, b) => a.FinishReport.Position.CompareTo(b.FinishReport.Position));

            for (int i = 0; i < movablePlayers.Count; i++)
            {
                Vector3 spawnpoint = lowerPositionsSpawnpoint.position;
                if (i < topPositionSpawnpoints.Length)
                {
                    spawnpoint = topPositionSpawnpoints[i].position;
                }
                RacePlayer playerToMove = movablePlayers[i];
                if (!movedPlayers.Contains(playerToMove))
                {
                    playerToMove.Ball.transform.position = spawnpoint;
                    playerToMove.Ball.transform.rotation = transform.rotation;
                    playerToMove.Ball.GetComponent<Rigidbody>().velocity = Random.insideUnitSphere * 0.5f;
                    playerToMove.Ball.GetComponent<Rigidbody>().angularVelocity = new Vector3(0, Random.Range(-50f, 50f));
                    playerToMove.Ball.CanMove = false;
                    playerToMove.Ball.gameObject.layer = LayerMask.NameToLayer("Racer");
                    movedPlayers.Add(playerToMove);
                }
            }
        }

        private void ExportResults(RaceManager manager)
        {
            try
            {
                string rootPath = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
                string logDir = Path.Combine(rootPath, "log");
                Directory.CreateDirectory(logDir);

                string mapName = ActiveData.Stages[manager.Settings.StageId].name;
                foreach (char c in Path.GetInvalidFileNameChars())
                {
                    mapName = mapName.Replace(c, '_');
                }
                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string filePath = Path.Combine(logDir, string.Format("{0}_{1}.txt", mapName, timestamp));

                using (var writer = new StreamWriter(filePath, false))
                {
                    writer.WriteLine("Placement\tPseudo\tTime");
                    List<RacePlayer> players = new List<RacePlayer>();
                    for (int i = 0; i < manager.PlayerCount; i++)
                    {
                        RacePlayer rp = manager[i];
                        if (rp.RaceFinished && !rp.FinishReport.Disqualified)
                        {
                            players.Add(rp);
                        }
                    }
                    players.Sort((a, b) => a.FinishReport.Position.CompareTo(b.FinishReport.Position));
                    foreach (RacePlayer p in players)
                    {
                        string pos = p.FinishReport.Position.ToString();
                        string name = p.Name;
                        string time = Utils.GetTimeString(p.FinishReport.Time);
                        writer.WriteLine(string.Format("{0}\t{1}\t{2}", pos, name, time));
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError("Failed to export results: " + ex.Message);
            }
        }
    }
}