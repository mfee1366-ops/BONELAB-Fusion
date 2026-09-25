using LabFusion.Extensions;

using UnityEngine;

namespace LabFusion.Entities;

/// <summary>
/// Snapshot interpolation for a remote player's head, hands, and playspace rotation.
/// Poses arrive at uneven rates (adaptive rates, host rate limits, far players at 2 Hz, and network
/// jitter), so chasing the newest pose makes avatars step and stall. This renders slightly in the past
/// and blends between the two poses around that time, with the delay sized to the player's own update rate.
/// </summary>
public sealed class RigMotionSmoother
{
    private const int Capacity = 8;

    // Render this many average update intervals behind, so the next pose has usually arrived
    private const float IntervalsBehind = 1.5f;
    private const float MinDelay = 0.05f;
    private const float MaxDelay = 0.35f;

    // How quickly the average update interval follows changes in the sender's rate
    private const float IntervalAdaptRate = 0.2f;

    // Gaps longer than this (e.g. the player was out of range) aren't counted as their update rate
    private const float MaxCountedInterval = 1f;

    private sealed class Snapshot
    {
        public float Time;
        public Vector3[] Positions;
        public Quaternion[] Rotations;
        public Quaternion Playspace;
    }

    private readonly Snapshot[] _snapshots = new Snapshot[Capacity];
    private readonly int _pointCount;

    private int _newest = -1;
    private int _count = 0;

    private float _averageInterval = 0.05f;

    private readonly Vector3[] _sampledPositions;
    private readonly Quaternion[] _sampledRotations;
    private Quaternion _sampledPlayspace = Quaternion.identity;
    private int _sampledFrame = -1;
    private bool _hasSample = false;

    public bool HasSamples => _count > 0;

    public RigMotionSmoother(int pointCount)
    {
        _pointCount = pointCount;

        for (var i = 0; i < Capacity; i++)
        {
            _snapshots[i] = new Snapshot()
            {
                Positions = new Vector3[pointCount],
                Rotations = new Quaternion[pointCount],
            };
        }

        _sampledPositions = new Vector3[pointCount];
        _sampledRotations = new Quaternion[pointCount];
    }

    public void Reset()
    {
        _newest = -1;
        _count = 0;
        _sampledFrame = -1;
        _hasSample = false;
    }

    public void AddSample(RigPose pose)
    {
        float now = Time.unscaledTime;

        if (_count > 0)
        {
            float interval = now - _snapshots[_newest].Time;

            // Several poses in one frame share a timestamp. Replace the newest instead of adding a zero-length step.
            if (interval <= 0f)
            {
                Write(_snapshots[_newest], pose, now);
                return;
            }

            if (interval < MaxCountedInterval)
            {
                _averageInterval = Mathf.Lerp(_averageInterval, interval, IntervalAdaptRate);
            }
        }

        _newest = (_newest + 1) % Capacity;
        _count = System.Math.Min(_count + 1, Capacity);

        Write(_snapshots[_newest], pose, now);
    }

    private void Write(Snapshot snapshot, RigPose pose, float time)
    {
        snapshot.Time = time;
        snapshot.Playspace = pose.TrackedPlayspaceExpanded.YawOnly();

        for (var i = 0; i < _pointCount; i++)
        {
            var point = pose.TrackedPoints[i];
            snapshot.Positions[i] = point.position;
            snapshot.Rotations[i] = point.rotation;
        }
    }

    /// <summary>
    /// Gets the interpolated tracked points and playspace rotation for this frame.
    /// </summary>
    public bool TryGetSample(out Vector3[] positions, out Quaternion[] rotations, out Quaternion playspace)
    {
        int frame = Time.frameCount;

        // The playspace and tracked points are read at different points in the frame, so only sample once
        if (frame != _sampledFrame)
        {
            _sampledFrame = frame;
            _hasSample = Evaluate(Time.unscaledTime);
        }

        positions = _sampledPositions;
        rotations = _sampledRotations;
        playspace = _sampledPlayspace;

        return _hasSample;
    }

    private bool Evaluate(float now)
    {
        if (_count <= 0)
        {
            return false;
        }

        float delay = Mathf.Clamp(_averageInterval * IntervalsBehind, MinDelay, MaxDelay);
        float renderTime = now - delay;

        // Walk from newest to oldest to find the pose just before the render time
        Snapshot newer = _snapshots[_newest];
        Snapshot older = null;

        for (var i = 0; i < _count; i++)
        {
            var snapshot = _snapshots[(_newest - i + Capacity) % Capacity];

            if (snapshot.Time <= renderTime)
            {
                older = snapshot;
                break;
            }

            newer = snapshot;
        }

        // Render time is past the newest pose (updates stalled): hold the newest.
        // Render time is before every buffered pose: hold the oldest.
        if (older == null || older == newer)
        {
            var hold = older ?? newer;
            Copy(hold);
            return true;
        }

        float t = Mathf.Clamp01((renderTime - older.Time) / (newer.Time - older.Time));

        for (var i = 0; i < _pointCount; i++)
        {
            _sampledPositions[i] = Vector3.Lerp(older.Positions[i], newer.Positions[i], t);
            _sampledRotations[i] = Quaternion.Slerp(older.Rotations[i], newer.Rotations[i], t);
        }

        _sampledPlayspace = Quaternion.Slerp(older.Playspace, newer.Playspace, t);

        return true;
    }

    private void Copy(Snapshot snapshot)
    {
        for (var i = 0; i < _pointCount; i++)
        {
            _sampledPositions[i] = snapshot.Positions[i];
            _sampledRotations[i] = snapshot.Rotations[i];
        }

        _sampledPlayspace = snapshot.Playspace;
    }
}
