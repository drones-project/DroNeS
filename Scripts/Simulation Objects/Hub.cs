﻿using System.Collections.Generic;
using Mapbox.Utils;
using UnityEngine;
using System.Collections;
using System;

namespace Drones
{
    using Utils;
    using Utils.Extensions;
    using DataStreamer;
    using Drones.UI;

    public class Hub : MonoBehaviour, IDronesObject, IDataSource
    {
        public static float AverageChargingVoltage { get; } = 4;
        #region IDataSource
        public SecureSet<ISingleDataSourceReceiver> Connections
        {
            get
            {
                if (_Connections == null)
                {
                    _Connections = new SecureSet<ISingleDataSourceReceiver>
                    {
                        MemberCondition = (ISingleDataSourceReceiver obj) => obj is ListTuple || obj is HubWindow
                    };
                }
                return _Connections;
            }
        }

        public int TotalConnections
        {
            get
            {
                return Connections.Count;
            }
        }

        public string[] GetData(WindowType windowType)
        {
            string[] output;
            if (windowType == WindowType.Hub)
            {
                output = new string[10];
                output[0] = HubStatus.ToString();
                output[1] = Name;
                output[2] = StaticFunc.CoordString(Location);
                output[3] = ActiveDroneCount.ToString();
                output[4] = IdleDroneCount.ToString();
                output[5] = ActiveDroneCount.ToString();
                output[6] = (IdleDroneCount + IdleBatteryCount).ToString();
                output[7] = PowerUse.ToString("0.00");
                output[8] = EnergyUse.ToString("0.00");
            } 
            else if (windowType == WindowType.HubList)
            {
                output = new string[5];
                output[0] = HubStatus.ToString();
                output[1] = Name;
                output[2] = DroneCount.ToString();
                output[3] = BatteryCount.ToString();
                output[4] = StaticFunc.CoordString(Location);
            }
            else
            {
                throw new ArgumentException("Wrong Window Type Supplied");
            }
            return output;
        }
        public AbstractInfoWindow InfoWindow { get; set; }

        public void OpenInfoWindow()
        {
            if (InfoWindow == null)
            {
                InfoWindow = (HubWindow)UIObjectPool.Get(WindowType.Hub, Singletons.UICanvas);
                InfoWindow.Source = this;
                Connections.Add(InfoWindow);
            }
            else
            {
                InfoWindow.transform.SetAsLastSibling();
            }
        }

        #endregion

        #region IDronesObject
        public string Name { get; set; }

        public Job AssignedJob { get; set; }

        public Hub AssignedHub { get; set; }

        public Drone AssignedDrone { get; } = null;
        #endregion

        #region Fields
        public SecureSet<ISingleDataSourceReceiver> _Connections;

        private SecureSet<IDataSource> _Drones;

        private SecureSet<Drone> _IdleDrones;

        private SecureSet<Battery> _ChargingBatteries;

        private SecureSet<Battery> _IdleBatteries;
        #endregion

        void OnEnable()
        {
            //TODO
        }

        public Status HubStatus { get; set; } = Status.Green;

        public SecureSet<IDataSource> Drones 
        { 
            get 
            { 
                if (_Drones == null)
                {
                    _Drones = new SecureSet<IDataSource>
                    {
                        MemberCondition = (IDataSource obj) => { return obj is Drone; }
                    };
                }
                return _Drones;
            } 
        }

        public SecureSet<Drone> IdleDrones
        {
            get
            {
                if (_IdleDrones == null)
                {
                    _IdleDrones = new SecureSet<Drone>();
                }
                return _IdleDrones;
            }
        }

        public SecureSet<Battery> ChargingBatteries
        {
            get
            {
                if (_ChargingBatteries == null)
                {
                    _ChargingBatteries = new SecureSet<Battery>
                    {
                        MemberCondition = (Battery obj) => { return obj.Status == BatteryStatus.Idle; }
                    };
                }
                return _ChargingBatteries;
            }
        }

        // This excludes batteries in idle drones
        public SecureSet<Battery> IdleBatteries
        {
            get
            {
                if (_IdleBatteries == null)
                {
                    _IdleBatteries = new SecureSet<Battery>
                    {
                        MemberCondition = (Battery obj) => { return obj.Status == BatteryStatus.Idle; }
                    };
                }
                return _IdleBatteries;
            }
        }

        public Vector2 Location
        {
            get
            {
                return transform.position.ToCoordinates();
            }
        }

        public int DroneCount
        {
            get
            {
                return Drones.Count;
            }
        }

        public int ActiveDroneCount { get; private set; } = 0;

        public int IdleDroneCount
        {
            get
            {
                return DroneCount - ActiveDroneCount;
            }
        }

        public int BatteryCount
        {
            get
            {
                return DroneCount + IdleBatteryCount + ChargingBatteryCount;
            }
        }

        public int IdleBatteryCount
        {
            get
            {
                return IdleBatteries.Count;
            }
        }

        public int ChargingBatteryCount
        {
            get
            {
                return ChargingBatteries.Count;
            }
        }

        // Watts
        public float PowerUse
        {
            get
            {
                return ChargingBatteryCount * Battery.ChargeRate / 1000 * AverageChargingVoltage;
            }
        }

        // Joules
        public float EnergyUse { get; private set; }

        IEnumerator IntegrateForEnergy()
        {
            var wait = new WaitForSeconds(1 / 60f);
            TimeKeeper.Chronos prev = TimeKeeper.Chronos.Get();
            float dt;
            while (true)
            {
                dt = prev.Timer();

                EnergyUse += PowerUse * dt;
                prev.Now();

                yield return wait;
            }
        }

        private void OnDisable()
        {
            Connections.Clear();
        }

        public void OnDroneJobAssignmentChange(Drone drone)
        {
            if (drone.AssignedJob != null)
            {
                ActiveDroneCount++;
            }
            else
            {
                ActiveDroneCount--;
            }
        }

        public override bool Equals(object other)
        {
            return other is Hub && GetHashCode() == other.GetHashCode();
        }

        public override int GetHashCode()
        {
            return GetInstanceID();
        }
    };
}