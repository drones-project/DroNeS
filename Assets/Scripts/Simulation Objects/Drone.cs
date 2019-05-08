﻿using UnityEngine;
using System.Linq;
using System;
using System.Collections.Generic;

namespace Drones
{
    using Managers;
    using DataStreamer;
    using Interface;
    using Serializable;
    using UI;
    using Utils;
    using Utils.Extensions;

    public class Drone : MonoBehaviour, IDronesObject, IDataSource, IPoolable
    {
        public const float DroneAndBatteryMass = 22.5f;

        public static void Reset() => _Count = 0;

        private static uint _Count;

        public static Drone New() => (Drone)ObjectPool.Get(typeof(Drone));

        public static Drone Load(SDrone data)
        {
            var d = (Drone)ObjectPool.Get(typeof(Drone), true);
            d.gameObject.SetActive(true);
            d.LoadState(data);
            d.LoadAssignments(data);

            return d;
        }

        #region IPoolable
        public void Delete() => ObjectPool.Release(this);

        public void OnRelease()
        {
            StopAllCoroutines();
            InPool = true;
            InfoWindow?.Close.onClick.Invoke();
            if (AssignedJob != null)
            {
                AssignedJob.FailJob();
                AssignedJob = null;
            }
            AssignedBattery = null;
            SimManager.AllDrones.Remove(this);
            AssignedHub = null;

            CompletedJobs.Clear();
            Connections.Clear();
            gameObject.SetActive(false);
            transform.SetParent(ObjectPool.PoolContainer);
        }

        public void OnGet(Transform parent = null)
        {
            UID = ++_Count;
            Trail.enabled = true;
            Name = "D" + UID.ToString("000000");
            SimManager.AllDrones.Add(UID, this);
            transform.SetParent(parent);
            gameObject.SetActive(true);
            Movement = DroneMovement.Idle;
            _state = FlightStatus.Idle;
            CollisionOn = false;
            JobManager.AddToQueue(this);
            InPool = false;
            PreviousPosition = transform.position;
        }

        public bool InPool { get; private set; }
        #endregion

        #region IDataSource
        public bool IsDataStatic { get; } = false;

        public AbstractInfoWindow InfoWindow { get; set; }

        public SecureSortedSet<int, ISingleDataSourceReceiver> Connections
        {
            get
            {
                if (_Connections == null)
                {
                    _Connections = new SecureSortedSet<int, ISingleDataSourceReceiver>((x, y) => (x.OpenTime <= y.OpenTime) ? -1 : 1)
                    {
                        MemberCondition = (ISingleDataSourceReceiver obj) => obj is ListTuple || obj is DroneWindow
                    };
                }
                return _Connections;
            }
        }

        private readonly string[] infoOutput = new string[28];
        private readonly string[] listOutput = new string[4];

        public string[] GetData(WindowType windowType)
        {
            if (windowType == WindowType.Drone)
            {
                infoOutput[0] = Name;
                infoOutput[1] = AssignedHub.Name;
                infoOutput[2] = Waypoint.ToStringXZ();
                infoOutput[3] = UnitConverter.Convert(Length.m, transform.position.y);
                if (AssignedBattery != null)
                {
                    infoOutput[4] = AssignedBattery.Charge.ToString("0.000");
                    infoOutput[5] = AssignedBattery.Capacity.ToString("0.000");
                }
                else
                {
                    for (int i = 4; i < 6; i++) infoOutput[i] = "0.000";
                }

                if (AssignedJob != null)
                {
                    infoOutput[6] = AssignedJob.Name;
                    infoOutput[7] = AssignedJob.Pickup.ToStringXZ();
                    infoOutput[8] = AssignedJob.Pickup.ToStringXZ();
                    infoOutput[9] = AssignedJob.Deadline.ToString();
                    infoOutput[10] = UnitConverter.Convert(Mass.g, AssignedJob.PackageWeight);
                    infoOutput[11] = "$" + AssignedJob.Earnings.ToString("0.00");
                    infoOutput[12] = JobProgress.ToString("0.000");
                } 
                else
                {
                    for (int i = 6; i < 12; i++)
                        infoOutput[i] = "";

                    infoOutput[12] = "0.000";
                }

                infoOutput[13] = DeliveryCount.ToString();
                infoOutput[14] = UnitConverter.Convert(Mass.kg, PackageWeight);
                infoOutput[15] = UnitConverter.Convert(Length.km, DistanceTravelled);
                float tmp = UnitConverter.ConvertValue(Mass.kg, PackageWeight);
                tmp /= UnitConverter.ConvertValue(Length.km, DistanceTravelled);
                infoOutput[16] = tmp.ToString("0.000") + " " + Mass.kg + "/" + Length.km;
                infoOutput[17] = UnitConverter.Convert(Energy.kWh, TotalEnergy);
                infoOutput[18] = BatterySwaps.ToString();
                infoOutput[19] = HubHandovers.ToString();
                infoOutput[20] = UnitConverter.Convert(Chronos.min, AudibleDuration);

                //Averages
                infoOutput[21] = UnitConverter.Convert(Mass.kg, PackageWeight / DeliveryCount);
                infoOutput[22] = UnitConverter.Convert(Length.km, DistanceTravelled / DeliveryCount);
                infoOutput[23] = UnitConverter.Convert(Chronos.min, TotalDelay);
                infoOutput[24] = UnitConverter.Convert(Energy.kWh, TotalEnergy / DeliveryCount);
                tmp = BatterySwaps;
                tmp /= DeliveryCount;
                infoOutput[25] = tmp.ToString();
                tmp = HubHandovers;
                tmp /= DeliveryCount;
                infoOutput[26] = tmp.ToString();
                infoOutput[27] = UnitConverter.Convert(Chronos.min, AudibleDuration);

                return infoOutput;
            }
            if (windowType == WindowType.DroneList)
            {
                listOutput[0] = Name;
                listOutput[1] = AssignedHub.Name;
                if (AssignedJob != null)
                {
                    listOutput[2] = AssignedJob.Pickup.ToStringXZ();
                    listOutput[3] = AssignedJob.Dest.ToStringXZ();
                }
                return listOutput;
            }
            throw new ArgumentException("Wrong Window Type Supplied!");
        }

        public void OpenInfoWindow()
        {
            if (InfoWindow == null)
            {
                InfoWindow = (DroneWindow)UIObjectPool.Get(WindowType.Drone, Singletons.UICanvas);
                InfoWindow.Source = this;
                Connections.Add(InfoWindow.UID, InfoWindow);
            }
            else
            {
                InfoWindow.transform.SetAsLastSibling();
            }

        }
        #endregion

        #region IDronesObject
        public string Name { get; private set; }

        public uint UID { get; private set; }

        public Job AssignedJob
        {
            get
            {
                return _AssignedJob;
            }
            set
            {
                if ((_AssignedJob == null && value != null) || (_AssignedJob != null && value == null))
                {
                    _AssignedJob = value;
                    OnJobAssign();
                }
            }
        }

        public Hub AssignedHub
        {
            get
            {
                return _AssignedHub;
            }
            set
            {
                if (value != null)
                {
                    _AssignedHub = value;
                    HubHandovers++;
                }
            }
        }

        public Drone AssignedDrone => this;
        #endregion

        #region Fields
        private Job _AssignedJob;
        private Hub _AssignedHub;
        private AudioSensor _Sensor;
        private SecureSortedSet<uint, IDataSource> _CompletedJobs;
        private SecureSortedSet<int, ISingleDataSourceReceiver> _Connections;
        private FlightStatus _state = FlightStatus.Idle;
        private Queue<Vector3> _waypoints = new Queue<Vector3>();
        private Battery _AssignedBattery;
        private Vector3 _PreviousWaypoint;
        private TrailRenderer _Trail;
        public static float minAlt = 150;
        public static float maxAlt = 480;
        #endregion

        #region Drone Properties
        public uint DeliveryCount { get; private set; }
        public float PackageWeight { get; private set; }
        public float DistanceTravelled { get; private set; }
        public uint BatterySwaps { get; private set; }
        public uint HubHandovers { get; private set; }
        public float TotalDelay { get; private set; }
        public float AudibleDuration { get; private set; }
        public float TotalEnergy { get; set; }

        public TrailRenderer Trail
        {
            get
            {
                if (_Trail == null)
                {
                    _Trail = GetComponent<TrailRenderer>();
                }
                return _Trail;
            }
        }
        public AudioSensor Sensor
        {
            get
            {
                if (_Sensor == null)
                {
                    _Sensor = GetComponentInChildren<AudioSensor>();
                }
                return _Sensor;
            }
        }

        public bool InHub { get; private set; }

        public Vector3 PreviousPosition { get; set; }

        public SecureSortedSet<uint, IDataSource> CompletedJobs
        {
            get
            {
                if (_CompletedJobs == null)
                {
                    _CompletedJobs = new SecureSortedSet<uint, IDataSource>
                        ((x, y) => (((Job)x).CompletedOn >= ((Job)y).CompletedOn) ? -1 : 1)
                    {
                        MemberCondition = (IDataSource obj) => { return obj is Job; }
                    };
                    _CompletedJobs.ItemAdded += (obj) => SimManager.AllCompleteJobs.Add(obj.UID, obj);
                    _CompletedJobs.ItemAdded += (obj) => DeliveryCount++;
                    _CompletedJobs.ItemAdded += (obj) => PackageWeight += ((Job)obj).PackageWeight;
                }
                return _CompletedJobs;
            }
        }

        public Vector3 Position => transform.position;

        public float HubDistance
        {
            get
            {
                return (AssignedHub != null) ? Vector3.Distance(transform.position, AssignedHub.transform.position) : float.NaN;
            }
        }

        public float JobProgress
        {
            get
            {
                if (AssignedJob != null)
                {
                    if (AssignedJob.Status == JobStatus.Delivering)
                    {
                        float a = Vector3.Distance(Position, AssignedJob.Dest);
                        float b = Vector3.Distance(AssignedJob.Pickup, AssignedJob.Dest);
                        return Mathf.Clamp(a / b, 0, 1);
                    }
                }
                return 0;
            }
        }

        public DroneMovement Movement { get; private set; } = DroneMovement.Idle;

        public Battery AssignedBattery
        {
            get => _AssignedBattery;

            set
            {
                _AssignedBattery = value;
                if (_AssignedBattery != null) BatterySwaps++;
            }
        }

        public bool CollisionOn { get; private set; }

        public bool IsWaiting { get; set; }

        public float TargetAltitude { get; private set; }

        public float MaxSpeed { get; private set; } = 22f;

        public Vector3 Waypoint { get; private set; }

        public SVector3 Direction
        {
            get
            {
                return Vector3.Normalize(PreviousPosition - transform.position);
            }
        }
        #endregion

        public override string ToString() => Name;

        public void OnTriggerEnter(Collider other)
        {
            if (other.gameObject.layer == 10) return;

            if (other.CompareTag("Hub") && other.GetComponent<Hub>() == AssignedHub)
            {
                Debug.Log("Collision Off");
                InHub = true;
                IsWaiting = true;
                CollisionOn = false;
            }
            if (CollisionOn)
            {
                DroneManager.movementJobHandle.Complete();
                AssignedHub.DestroyDrone(this, other);
            }
        }

        public void OnTriggerExit(Collider other)
        {
            if (other.gameObject.layer == 10) return;

            if (other.CompareTag("Hub") && other.GetComponent<Hub>() == AssignedHub)
            {
                Debug.Log("Collision On");
                CollisionOn = true;
                InHub = false;
            }
        }

        private void ChangeAltitude(float height)
        {
            if (Movement == DroneMovement.Hover)
            {
                Movement = (transform.position.y > height) ?
                    DroneMovement.Descend : DroneMovement.Ascend;
                TargetAltitude = height;
            }
        }

        private void MoveTo(Vector3 waypoint)
        {
            if (Movement == DroneMovement.Hover)
            {
                Movement = DroneMovement.Horizontal;
                DistanceTravelled += Vector3.Distance(_PreviousWaypoint, Waypoint);
                _PreviousWaypoint = Waypoint;
                Waypoint = waypoint;
            }
            else Debug.Log("Cannot move during unfinished move commmand.");
        }

        private bool ReachedWaypoint()
        {
            Vector3 a = transform.position;
            Vector3 b = Waypoint;
            a.y = b.y = 0;

            return Vector3.Distance(a, b) < 0.1f;
        }

        public void NavigateWaypoints(List<Vector3> waypoints)
        {
            _waypoints = new Queue<Vector3>(waypoints);
            Movement = DroneMovement.Hover;
            _state = FlightStatus.PreparingHeight;
            ChangeAltitude(_waypoints.Peek().y);
        }

        public void NavigateWaypoints(List<SVector3> waypoints)
        {
            _waypoints = new Queue<Vector3>();
            foreach (SVector3 waypoint in waypoints)
            {
                _waypoints.Enqueue(waypoint);
            }
            Movement = DroneMovement.Hover;
            _state = FlightStatus.PreparingHeight;
            ChangeAltitude(_waypoints.Peek().y);
        }

        public void UpdateDelay(float dt) => TotalDelay += dt;

        public void UpdateAudible(float dt) => AudibleDuration += dt;

        void ChangeState()
        {
            if (_state == FlightStatus.PreparingHeight)
            {
                if (transform.position.y < 5.5f)
                {
                    if (AssignedJob == null)
                    {
                        JobManager.AddToQueue(this);
                    }
                    else
                    {
                        AssignedJob.StartDelivery();
                    }
                    RouteManager.AddToQueue(this);
                    return;
                }
                _state = FlightStatus.AwaitingWaypoint;
            }

            if (_state != FlightStatus.AwaitingWaypoint && _state != FlightStatus.Delivering) return;

            if (_waypoints.Count > 0)
            {
                _state = FlightStatus.Delivering;
                Waypoint = _waypoints.Dequeue();
                MoveTo(Waypoint);
                return;
            }

            if (InHub && Vector3.Distance(Waypoint, AssignedHub.Position) < Vector3.kEpsilon)
            {
                _state = FlightStatus.Idle;
                Movement = DroneMovement.Idle;
                AssignedHub.OnDroneReturn(this);
                return;
            }

            if (AssignedJob != null)
            {
                if (AssignedJob.Status != JobStatus.Pickup && AssignedJob.Status != JobStatus.Delivering) return;

                Vector3 destination =
                    AssignedJob.Status == JobStatus.Pickup ? AssignedJob.Pickup :
                    AssignedJob.Status == JobStatus.Delivering ? AssignedJob.Dest :
                    Vector3.zero;

                destination.y = transform.position.y;

                if (Vector3.Distance(transform.position, destination) < 0.1f)
                {
                    destination.y = 5;
                    NavigateWaypoints(new List<Vector3> { destination });
                }
            }
        }

        void LateUpdate()
        {
            if (Movement == DroneMovement.Ascend && transform.position.y >= TargetAltitude ||
                Movement == DroneMovement.Descend && transform.position.y <= TargetAltitude ||
                Movement == DroneMovement.Horizontal && ReachedWaypoint())
            {
                Movement = DroneMovement.Hover;
            }

            if (Movement == DroneMovement.Hover) ChangeState();

            if (Movement != DroneMovement.Idle && AssignedBattery.Status == BatteryStatus.Dead) Drop();
        }

        void Drop()
        {
            Trail.enabled = false;
            Movement = DroneMovement.Drop;
            if (AbstractCamera.Followee == gameObject) AbstractCamera.Followee = null;
        }

        public void OnJobAssign()
        {
            if (AssignedJob != null)
            {
                AssignedJob.AssignedDrone = this;
                RouteManager.AddToQueue(this);
                if (InHub)
                {
                    AssignedHub.ExitingDrones.Enqueue(this);
                }
            }
        }

        public SDrone Serialize()
        {
            var output = new SDrone
            {
                count = _Count,
                uid = UID,
                totalDeliveryCount = DeliveryCount,
                totalBatterySwaps = BatterySwaps,
                totalHubHandovers = HubHandovers,
                collisionOn = CollisionOn,
                isWaiting = IsWaiting,
                inHub = InHub,
                name = Name,
                movement = Movement,
                status = _state,
                totalDelay = TotalDelay,
                totalAudibleDuration = AudibleDuration,
                totalPackageWeight = PackageWeight,
                totalDistanceTravelled = DistanceTravelled,
                totalEnergy = TotalEnergy,
                targetAltitude = TargetAltitude,
                waypointsQueue = new List<SVector3>(),
                completedJobs = new List<uint>(),
                maxSpeed = MaxSpeed,
                position = transform.position,
                previousWaypoint = _PreviousWaypoint,
                waypoint = Waypoint,
                job = (AssignedJob == null) ? 0 : AssignedJob.UID,
                hub = (AssignedHub == null) ? 0 : AssignedHub.UID,
                hubPosition = (AssignedHub == null) ? 10000 * Vector3.one : AssignedHub.transform.position,
                battery = (AssignedBattery == null) ? 0 : AssignedBattery.UID,
                charge = (AssignedBattery == null) ? 0 : AssignedBattery.Charge
            };

            foreach (var point in _waypoints)
                output.waypointsQueue.Add(point);
            foreach (Job job in CompletedJobs.Values)
                output.completedJobs.Add(job.UID);

            return output;
        }

        public Drone LoadState(SDrone data)
        {
            _Count = data.count;
            UID = data.uid;
            InPool = false;
            DeliveryCount = data.totalDeliveryCount;
            BatterySwaps = data.totalBatterySwaps;
            HubHandovers = data.totalHubHandovers;
            CollisionOn = data.collisionOn;
            IsWaiting = data.isWaiting;
            Name = data.name;
            Movement = data.movement;
            _state = data.status;
            TotalDelay = data.totalDelay;
            AudibleDuration = data.totalAudibleDuration;
            PackageWeight = data.totalPackageWeight;
            DistanceTravelled = data.totalDistanceTravelled;
            TotalEnergy = data.totalEnergy;
            TargetAltitude = data.targetAltitude;
            PreviousPosition = transform.position;
            _waypoints = new Queue<Vector3>();
            foreach (Vector3 point in data.waypointsQueue)
            {
                _waypoints.Enqueue(point);
            }
            Waypoint = data.waypoint;
            _PreviousWaypoint = data.previousWaypoint;
            transform.position = data.position;
            return this;
        }

        public Drone LoadAssignments(SDrone data)
        {
            _AssignedBattery = SimManager.AllBatteries[data.battery];
            _AssignedJob = (Job)SimManager.AllIncompleteJobs[data.job];
            if (CompletedJobs == null) { }
            foreach (uint id in data.completedJobs)
                _CompletedJobs.Add(id, SimManager.AllCompleteJobs[id]);
            DeliveryCount = data.totalDeliveryCount;
            PackageWeight = data.totalPackageWeight;

            if (_AssignedBattery != null)
                _AssignedBattery.AssignedDrone = this;
            if (_AssignedJob != null)
                _AssignedJob.AssignedDrone = this;
            // Request route from here to job destination
            return this;
        }

    };
}
