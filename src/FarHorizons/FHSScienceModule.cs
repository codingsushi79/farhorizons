using System.Collections.Generic;
using KSP.UI.Screens.Flight.Dialogs;

namespace FarHorizons
{
    /// <summary>
    /// Base for every Far Horizons instrument. Stores ScienceData the same way stock experiments do
    /// (as "ScienceData" child nodes), so it works with transmitters, the Mobile Processing Lab,
    /// EVA collection, Experiment Storage Units and vessel recovery.
    /// </summary>
    public abstract class FHSScienceModule : PartModule, IScienceDataContainer
    {
        /// <summary>Fraction of science returned when transmitting instead of recovering.</summary>
        [KSPField] public float xmitDataScalar = 0.3f;

        /// <summary>How many results this part can hold at once.</summary>
        [KSPField] public int maxStoredData = 3;

        /// <summary>Whether EVA kerbals can take the data out.</summary>
        [KSPField] public bool dataIsCollectable = true;

        [KSPField] public float interactionRange = 1.5f;

        protected readonly List<ScienceData> storedData = new List<ScienceData>();

        // ------------------------------------------------------------------
        // Persistence
        // ------------------------------------------------------------------

        public override void OnLoad(ConfigNode node)
        {
            base.OnLoad(node);
            storedData.Clear();
            foreach (ConfigNode d in node.GetNodes("ScienceData"))
                storedData.Add(new ScienceData(d));
        }

        public override void OnSave(ConfigNode node)
        {
            base.OnSave(node);
            foreach (ScienceData d in storedData)
                d.Save(node.AddNode("ScienceData"));
        }

        public override void OnStart(StartState state)
        {
            base.OnStart(state);
            Events["CollectDataExternal"].guiActiveUnfocused = dataIsCollectable;
            Events["CollectDataExternal"].unfocusedRange = interactionRange;
            UpdateDataEvents();
        }

        // ------------------------------------------------------------------
        // PAW events
        // ------------------------------------------------------------------

        [KSPEvent(guiActive = true, guiName = "Review Stored Data", active = false)]
        public void ReviewDataEvent()
        {
            ReviewData();
        }

        [KSPEvent(guiActive = false, guiActiveUnfocused = true, externalToEVAOnly = true, unfocusedRange = 1.5f,
            guiName = "Take Data", active = false)]
        public void CollectDataExternal()
        {
            Vessel eva = FlightGlobals.ActiveVessel;
            if (eva == null || !eva.isEVA) return;
            ModuleScienceContainer container = eva.FindPartModuleImplementing<ModuleScienceContainer>();
            if (container == null) return;

            int moved = 0;
            foreach (ScienceData d in storedData.ToArray())
            {
                if (container.HasData(d)) continue;
                if (container.AddData(d))
                {
                    storedData.Remove(d);
                    moved++;
                }
            }
            FHSUtil.Message(moved > 0
                ? string.Format("{0} took {1} result(s) from {2}.", eva.GetDisplayName(), moved, part.partInfo.title)
                : "Nothing new to take (duplicates are left behind).");
            UpdateDataEvents();
        }

        protected virtual void UpdateDataEvents()
        {
            bool has = storedData.Count > 0;
            Events["ReviewDataEvent"].active = has;
            Events["CollectDataExternal"].active = has && dataIsCollectable;
        }

        protected bool StorageFull
        {
            get { return storedData.Count >= maxStoredData; }
        }

        protected bool HasSubject(string subjectID)
        {
            foreach (ScienceData d in storedData)
                if (d.subjectID == subjectID) return true;
            return false;
        }

        protected void StoreAndReview(ScienceData data)
        {
            storedData.Add(data);
            UpdateDataEvents();
            ReviewDataItem(data);
        }

        // ------------------------------------------------------------------
        // IScienceDataContainer
        // ------------------------------------------------------------------

        public ScienceData[] GetData()
        {
            return storedData.ToArray();
        }

        public int GetScienceCount()
        {
            return storedData.Count;
        }

        public virtual bool IsRerunnable()
        {
            return true;
        }

        public void ReturnData(ScienceData data)
        {
            if (data == null || storedData.Contains(data)) return;
            storedData.Add(data);
            UpdateDataEvents();
        }

        public void DumpData(ScienceData data)
        {
            storedData.Remove(data);
            UpdateDataEvents();
        }

        public void ReviewData()
        {
            foreach (ScienceData d in storedData.ToArray())
                ReviewDataItem(d);
        }

        public void ReviewDataItem(ScienceData data)
        {
            var page = new ExperimentResultDialogPage(
                part, data, data.baseTransmitValue, data.transmitBonus,
                false, string.Empty, false,
                new ScienceLabSearch(vessel, data),
                OnDiscardData, OnKeepData, OnTransmitData, OnSendToLab);
            ExperimentsResultDialog.DisplayResult(page);
        }

        // ------------------------------------------------------------------
        // Result dialog callbacks
        // ------------------------------------------------------------------

        private void OnDiscardData(ScienceData data)
        {
            DumpData(data);
        }

        private void OnKeepData(ScienceData data)
        {
        }

        private void OnTransmitData(ScienceData data)
        {
            IScienceDataTransmitter transmitter = ScienceUtil.GetBestTransmitter(vessel);
            if (transmitter == null)
            {
                FHSUtil.Message("No antenna on this vessel can transmit the data.");
                return;
            }
            transmitter.TransmitData(new List<ScienceData> { data });
            DumpData(data);
        }

        private void OnSendToLab(ScienceData data)
        {
            var search = new ScienceLabSearch(vessel, data);
            if (search.NextLabForDataFound)
                StartCoroutine(search.NextLabForData.ProcessData(data, OnLabProcessed));
            else
                search.PostErrorToScreen();
        }

        private void OnLabProcessed(ScienceData data)
        {
            DumpData(data);
        }
    }
}
