﻿using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;


namespace Drones.Managers
{

    public class JobManager : MonoBehaviour
    {
        [SerializeField]
        private static string _schedulerServerURL = "http://127.0.0.1:5000/jobs";
        private static Queue<Drone> _waitingList = new Queue<Drone>();

        void Start()
        {
            StartCoroutine(ProcessQueue());
        }

        IEnumerator ProcessQueue()
        {
            while (true)
            {
                yield return new WaitUntil(() => _waitingList.Count > 0);
                // we recheck the condition here in case of spurious wakeups
                if (_waitingList.Count > 0)
                {
                    Drone drone = _waitingList.Dequeue();
                    StartCoroutine(GetJob(drone));
                }
            }
        }

        IEnumerator GetJob(Drone drone)
        {
            Debug.Log("Fetching a job...");
            Drones.Serializable.SSimulation game_state = SimManager.SerializeSimulation();

            var request = new UnityWebRequest(_schedulerServerURL, "POST");
            byte[] bodyRaw = Encoding.UTF8.GetBytes(JsonUtility.ToJson(game_state));
            request.uploadHandler = (UploadHandler)new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = (DownloadHandler)new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");

            yield return request.SendWebRequest();

            if (request.responseCode == 200)
            {
                Serializable.SJob s_job = JsonUtility.FromJson<Serializable.SJob>(request.downloadHandler.text);
                Debug.Log("Job: " + JsonUtility.ToJson(s_job));
                drone.AssignedJob = new Job(s_job);
            }
        }

        public static void AddToQueue(Drone drone)
        {
            _waitingList.Enqueue(drone);
        }
    }
}