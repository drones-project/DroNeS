﻿using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using System.Linq;

namespace Drones.Managers
{
    using Utils;
    using Serializable;

    public class RouteManager : MonoBehaviour
    {
        private static RouteManager Instance { get; set; }

        public const string DEFAULT_URL = "localhost:5000/route";

        public static string RouterURL { get; set; } = DEFAULT_URL;

        private readonly Queue<Drone> _waitingList = new Queue<Drone>();

        private bool _Started;

        private static bool Started
        {
            get => Instance._Started;
            set => Instance._Started = value;
        }

        private void Awake()
        {
            Instance = this;
        }

        private void OnDestroy()
        {
            Instance = null;
        }

        private IEnumerator ProcessQueue()
        {
            Started = true;
            while (true)
            {
                yield return new WaitUntil(() => (_waitingList.Count > 0) && (TimeKeeper.TimeSpeed != TimeSpeed.Pause));
                // we recheck the condition here in case of spurious wakeups
                RouterPayload payload = SimManager.GetRouterPayload();
                while (_waitingList.Count > 0 && TimeKeeper.TimeSpeed != TimeSpeed.Pause)
                {
                    Drone drone = _waitingList.Dequeue();

                    if (drone.InPool) continue;

                    StartCoroutine(GetRoute(drone, payload));
                    if (TimeKeeper.DeltaFrame() > 12)
                    {
                        yield return null;
                        payload = SimManager.GetRouterPayload();
                    }
                }
            }
        }

        private IEnumerator GetRoute(Drone drone, RouterPayload payload)
        {
            payload.requester = drone.UID;

            payload.origin = drone.transform.position;
            var job = drone.GetJob();
            payload.destination =
                job == null ? drone.GetHub().Position :
                job.Status == JobStatus.Pickup ? job.Pickup :
                job.Status == JobStatus.Delivering ? job.DropOff :
                drone.GetHub().Position;

            if (job != null)
            {
                payload.onJob = true;
                payload.status = job.Status;
            }

            var request = new UnityWebRequest(RouterURL, "POST")
            {
                uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(payload.ToJson())),
                downloadHandler = new DownloadHandlerBuffer(),
            };

            request.SetRequestHeader("Content-Type", "application/json");

            yield return request.SendWebRequest();


            if (request.responseCode == 200 && request.downloadHandler.text != "{}")
            {
                SRoute route = JsonUtility.FromJson<SRoute>(request.downloadHandler.text);
                if (route.waypoints != null && route.waypoints.Count != 0 && route.droneUID == drone.UID)
                    drone.ProcessRoute(route);
            }
            else //if (request.responseCode == 200)
            {
                yield return null;
                AddToQueue(drone);
            }
            //else
            //{
            //    SimManager.SimStatus = SimulationStatus.Paused;
            //    AddToQueue(drone);
            //}
        }

        public static void AddToQueue(Drone drone)
        {
            if (!Started)
            {
                Instance.StartCoroutine(Instance.ProcessQueue());
            }
            if (!Instance._waitingList.Contains(drone))
            {
                Instance._waitingList.Enqueue(drone);
            }
        }

        public static List<uint> Serialize()
        {
            var l = new List<uint>();
            foreach (var d in Instance._waitingList)
            {
                l.Add(d.UID);
            }
            return l;
        }

    }
}
