using System;
using System.Collections.Generic;
using UnityEngine;

namespace RemoteTech {
    public class ModuleSPU : PartModule, ISignalProcessor {
        public bool Powered {
            get { return mRegisteredId != Guid.Empty && IsPowered; }
        }

        public bool CommandStation {
            get { return Powered && IsRTCommandStation && Vessel.GetVesselCrew().Count >= 6; }
        }

        public Guid Guid {
            get { return Vessel == null ? Guid.Empty : Vessel.id; }
        }

        public Vessel Vessel {
            get { return vessel; }
        }

        public VesselSatellite Satellite {
            get {
                return mRegisteredId == Guid.Empty 
                    ? null 
                    : RTCore.Instance.Satellites[mRegisteredId];
            }
        }

        public FlightComputer FlightComputer { get; private set; }

        [KSPField(isPersistant = true)]
        public bool IsPowered = false;

        [KSPField(isPersistant = true)]
        public bool IsRTSignalProcessor = true;

        [KSPField(isPersistant = true)]
        public bool IsRTCommandStation = true;

        [KSPField]
        public int minimumCrew = 0;

        [KSPField(guiName = "State", guiActive = true)]
        public String Status;

        [KSPEvent(name = "OpenFC", active = true, guiActive = true, guiName = "Flight Computer")]
        public void OpenFC() {
            RTCore.Instance.Gui.OpenFlightComputer(this);
        }

        private enum State {
            Operational,
            NoCrew,
            NoResources,
            NoConnection
        }

        private Guid mRegisteredId;

        // Unity requires this to be public for some fucking magical reason?!
        public List<ModuleResource> RequiredResources;

        public override string GetInfo() {
            return IsRTCommandStation ? "Remote Command" : "Remote Control";
        }

        public override void OnStart(StartState state) {
            GameEvents.onVesselWasModified.Add(OnVesselModified);
            GameEvents.onPartUndock.Add(OnPartUndock);
            if (RTCore.Instance != null) {
                mRegisteredId = RTCore.Instance.Satellites.Register(Vessel, this);
            }
            if (FlightComputer == null) {
                FlightComputer = new FlightComputer(this);
            }
        }

        public void OnDestroy() {
            GameEvents.onVesselWasModified.Remove(OnVesselModified);
            GameEvents.onPartUndock.Remove(OnPartUndock);
            if (RTCore.Instance != null) {
                RTCore.Instance.Satellites.Unregister(mRegisteredId, this);
                mRegisteredId = Guid.Empty;
            }
            if (FlightComputer != null) {
                FlightComputer.Dispose();
            }
        }

        public override void OnLoad(ConfigNode node) {
            if (RequiredResources == null) {
                RequiredResources = new List<ModuleResource>();
            }
            foreach (ConfigNode cn in node.nodes) {
                if(!cn.name.Equals("RESOURCE")) continue;
                ModuleResource rs = new ModuleResource();
                rs.Load(cn);
                RequiredResources.Add(rs);
            }
        }

        private State UpdateControlState() {
            // Can't remove isControlSource or autopilot won't work.
            if (!RTCore.Instance) return State.NoConnection;
            if (part.protoModuleCrew.Count < minimumCrew) {
                IsPowered = part.isControlSource = false;
                return State.NoCrew;
            }
            foreach (ModuleResource rs in RequiredResources) {
                rs.currentRequest = rs.rate * TimeWarp.deltaTime;
                rs.currentAmount = part.RequestResource(rs.id, rs.currentRequest);
                if (rs.currentAmount < rs.currentRequest * 0.9) {
                    IsPowered = part.isControlSource = false;
                    return State.NoResources;
                }
            }
            IsPowered = part.isControlSource = true;
            if (Satellite == null || !Satellite.Connection.Exists) {
                return State.NoConnection;
            }
            return State.Operational;
        }

        public void FixedUpdate() {
            switch (UpdateControlState()) {
                case State.Operational:
                    Status = "Operational.";
                    break;
                case State.NoCrew:
                    Status = "Not enough crew.";
                    break;
                case State.NoConnection:
                    Status = "No connection.";
                    break;
                case State.NoResources:
                    Status = "Out of power";
                    break;
            }
        }

        public void OnPartUndock(Part p) {
            if (p.vessel == vessel) OnVesselModified(p.vessel);
        }

        public void OnVesselModified(Vessel v) {
            if (IsPowered) {
                if (vessel == null || (mRegisteredId != Vessel.id)) {
                    RTCore.Instance.Satellites.Unregister(mRegisteredId, this);
                    if (vessel != null) {
                        mRegisteredId = RTCore.Instance.Satellites.Register(Vessel, this); 
                    }
                }
            }
        }
    }
}
