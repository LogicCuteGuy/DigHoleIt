using UdonSharp;
using UnityEngine;
using VRC.SDK3.UdonNetworkCalling;
using VRC.SDKBase;
using VRC.Udon.Common.Interfaces;

namespace LogicCuteGuy.DigHoleIt.Udon
{
    /// <summary>
    /// Networks the edits of one DigZoneRuntime as an ordered, append-only log.
    ///
    /// Live path: a player applies its edit locally (prediction) and asks the owner to append it. The owner assigns
    /// the next sequence number and broadcasts it; every client applies edits strictly in sequence order, buffering
    /// any that arrive early. Dig/Add are idempotent, so re-applying a predicted edit changes nothing.
    ///
    /// Late joiners: the owner serializes the full log (Manual sync) shortly after someone joins; the joiner replays it.
    /// Every client keeps the full log, so an ownership handoff keeps working.
    ///
    /// Must live on its own GameObject: it uses Manual sync, the zone runtime uses NoVariableSync.
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class DigSync : UdonSharpBehaviour
    {
        private const int PendingCapacity = 128;

        public DigZoneRuntime zone;
        [Tooltip("Maximum edits per instance. 8 bytes each; the full log must stay under the ~280 KB Manual sync limit.")]
        public int capacity = 4096;
        [Tooltip("If set, only the instance master may reset the zone.")]
        public bool resetMasterOnly = true;

        [UdonSynced] private long[] _syncLog = new long[0];
        [UdonSynced] private int _syncEpoch;

        private long[] _log;
        private int _count;
        private int _epoch;

        private long[] _pendEdit;
        private int[] _pendSeq;
        private int[] _pendEpoch;
        private int _pendCount;

        private bool _serializeQueued;
        private float _lastSnapshotRequest = -100f;

        private void Start()
        {
            _log = new long[capacity];
            _pendEdit = new long[PendingCapacity];
            _pendSeq = new int[PendingCapacity];
            _pendEpoch = new int[PendingCapacity];
        }

        public int _GetCount() { return _count; }

        public bool _IsFull() { return _count >= capacity; }

        /// <summary>Local entry point used by DigZoneRuntime._LocalEdit.</summary>
        public void _Submit(long packed)
        {
            if (_log == null || _count >= capacity) return;
            zone._EnqueueEdit(packed);

            if (Networking.IsOwner(gameObject)) _OwnerAppend(packed);
            else SendCustomNetworkEvent(NetworkEventTarget.Owner, nameof(RequestEdit), packed);
        }

        [NetworkCallable(20)]
        public void RequestEdit(long packed)
        {
            if (!NetworkCalling.InNetworkCall) return;
            VRCPlayerApi caller = NetworkCalling.CallingPlayer;
            if (caller == null || !caller.IsValid()) return;
            if (!Networking.IsOwner(gameObject)) return;
            if (!zone._IsValidEdit(packed)) return;

            zone._EnqueueEdit(packed);
            _OwnerAppend(packed);
        }

        private void _OwnerAppend(long packed)
        {
            if (_count >= capacity) return;
            int seq = _count;
            _log[_count] = packed;
            _count++;
            SendCustomNetworkEvent(NetworkEventTarget.Others, nameof(ApplyRemote), packed, seq, _epoch);
        }

        [NetworkCallable(50)]
        public void ApplyRemote(long packed, int seq, int epoch)
        {
            if (!_IsFromOwner()) return;
            if (!zone._IsValidEdit(packed)) return;

            if (epoch != _epoch)
            {
                // A newer epoch before its snapshot arrived (late join or reset race): keep it for later.
                if (epoch > _epoch) _Buffer(packed, seq, epoch);
                return;
            }
            _Receive(packed, seq);
        }

        private void _Receive(long packed, int seq)
        {
            if (seq < _count) return; // already have it
            if (seq > _count)
            {
                _Buffer(packed, seq, _epoch);
                _RequestSnapshot();
                return;
            }
            _Append(packed);
            _DrainPending();
        }

        private void _Append(long packed)
        {
            if (_count >= capacity) return;
            _log[_count] = packed;
            _count++;
            zone._EnqueueEdit(packed);
        }

        private void _Buffer(long packed, int seq, int epoch)
        {
            if (_pendCount >= PendingCapacity) return; // the next snapshot fills the gap
            _pendEdit[_pendCount] = packed;
            _pendSeq[_pendCount] = seq;
            _pendEpoch[_pendCount] = epoch;
            _pendCount++;
        }

        private void _DrainPending()
        {
            bool progressed = true;
            while (progressed)
            {
                progressed = false;
                for (int k = 0; k < _pendCount; k++)
                {
                    bool stale = _pendEpoch[k] < _epoch || (_pendEpoch[k] == _epoch && _pendSeq[k] < _count);
                    bool next = _pendEpoch[k] == _epoch && _pendSeq[k] == _count;
                    if (!stale && !next) continue;

                    long e = _pendEdit[k];
                    _pendCount--;
                    _pendEdit[k] = _pendEdit[_pendCount];
                    _pendSeq[k] = _pendSeq[_pendCount];
                    _pendEpoch[k] = _pendEpoch[_pendCount];
                    k--;

                    if (next)
                    {
                        _Append(e);
                        progressed = true;
                    }
                }
            }
        }

        /// <summary>Asks the owner for a full snapshot when a gap in the sequence is detected (rate limited).</summary>
        private void _RequestSnapshot()
        {
            if (Time.time - _lastSnapshotRequest < 5f) return;
            _lastSnapshotRequest = Time.time;
            SendCustomNetworkEvent(NetworkEventTarget.Owner, nameof(OwnerSnapshot));
        }

        [NetworkCallable(1)]
        public void OwnerSnapshot()
        {
            if (!NetworkCalling.InNetworkCall) return;
            VRCPlayerApi caller = NetworkCalling.CallingPlayer;
            if (caller == null || !caller.IsValid()) return;
            if (Networking.IsOwner(gameObject)) _QueueSerialize();
        }

        private bool _IsFromOwner()
        {
            if (!NetworkCalling.InNetworkCall) return false;
            VRCPlayerApi caller = NetworkCalling.CallingPlayer;
            if (caller == null || !caller.IsValid()) return false;
            VRCPlayerApi owner = Networking.GetOwner(gameObject);
            return owner != null && owner.IsValid() && owner.playerId == caller.playerId;
        }

        // ---- Late joiners ----

        public override void OnPlayerJoined(VRCPlayerApi player)
        {
            if (player == null || player.isLocal) return;
            if (Networking.IsOwner(gameObject)) _QueueSerialize();
        }

        private void _QueueSerialize()
        {
            if (_serializeQueued) return;
            _serializeQueued = true;
            // Batch several joins into one snapshot.
            SendCustomEventDelayedSeconds(nameof(_DoSerialize), 1.5f);
        }

        public void _DoSerialize()
        {
            _serializeQueued = false;
            if (!Networking.IsOwner(gameObject)) return;
            _syncLog = new long[_count];
            System.Array.Copy(_log, _syncLog, _count);
            _syncEpoch = _epoch;
            RequestSerialization();
        }

        public override void OnDeserialization()
        {
            if (_log == null || _syncLog == null) return;

            if (_syncEpoch != _epoch)
            {
                if (_syncEpoch < _epoch) return; // stale snapshot from before a reset we already applied
                _LocalReset(_syncEpoch);
            }

            int n = Mathf.Min(_syncLog.Length, capacity);
            for (int i = _count; i < n; i++) _Append(_syncLog[i]);
            _DrainPending();
        }

        // ---- Reset ----

        /// <summary>Local entry point for a reset button (wire a UI or interact to call this).</summary>
        public void _RequestReset()
        {
            if (Networking.IsOwner(gameObject))
            {
                VRCPlayerApi lp = Networking.LocalPlayer;
                if (resetMasterOnly && (lp == null || !lp.isMaster)) return;
                _OwnerDoReset();
                return;
            }
            SendCustomNetworkEvent(NetworkEventTarget.Owner, nameof(OwnerReset));
        }

        [NetworkCallable(1)]
        public void OwnerReset()
        {
            if (!NetworkCalling.InNetworkCall) return;
            VRCPlayerApi caller = NetworkCalling.CallingPlayer;
            if (caller == null || !caller.IsValid()) return;
            if (resetMasterOnly && !caller.isMaster) return;
            if (!Networking.IsOwner(gameObject)) return;
            _OwnerDoReset();
        }

        private void _OwnerDoReset()
        {
            int epoch = _epoch + 1;
            _LocalReset(epoch);
            SendCustomNetworkEvent(NetworkEventTarget.Others, nameof(ApplyReset), epoch);
            _QueueSerialize();
        }

        [NetworkCallable(2)]
        public void ApplyReset(int epoch)
        {
            if (!_IsFromOwner()) return;
            if (epoch > _epoch) _LocalReset(epoch);
        }

        private void _LocalReset(int epoch)
        {
            _epoch = epoch;
            _count = 0;
            zone._ResetToOriginal();
            _DrainPending();
        }
    }
}
