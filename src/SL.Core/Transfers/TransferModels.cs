// SalmonLauncher (SL) — a Minecraft Java edition launcher
// Copyright (C) 2026 Hugo Mou
//
// This program is free software: you can redistribute it and/or modify it under
// the terms of the GNU General Public License as published by the Free Software
// Foundation, either version 3 of the License, or (at your option) any later version.
//
// This program is distributed in the hope that it will be useful, but WITHOUT ANY
// WARRANTY; without even the implied warranty of MERCHANTABILITY or FITNESS FOR A
// PARTICULAR PURPOSE. See the GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License along with
// this program. If not, see <https://www.gnu.org/licenses/>.
//
// SPDX-License-Identifier: GPL-3.0-or-later
namespace SL.Core.Transfers;

/// <summary>Task state machine: waiting → downloading → (verifying, stage 2) → completed; failed/paused/cancelled are side paths.</summary>
public enum TransferState
{
    Queued = 0,
    Paused,
    Downloading,
    Verifying,
    Completed,
    Failed,
    Cancelled,
}

public enum TransferKind
{
    /// <summary>Simulated data flow for demos (stage 1)</summary>
    Simulated = 0,

    /// <summary>Real HTTP download (wired up in stage 2)</summary>
    Http,
}

/// <summary>One entry of the multi-source list: the first is the primary source and the rest are mirrors (ordered dynamically by historical success rate).</summary>
public sealed class TransferSourceSpec
{
    public string Url { get; }
    public bool IsMirror { get; }

    public TransferSourceSpec(string url, bool isMirror = false)
    {
        Url = url;
        IsMirror = isMirror;
    }
}

/// <summary>Task description submitted to the engine (immutable input).</summary>
public sealed class TransferSpec
{
    public required string DisplayName { get; init; }
    public TransferKind Kind { get; init; } = TransferKind.Simulated;

    /// <summary>Priority: larger values are scheduled first (game core files > libraries > assets)</summary>
    public int Priority { get; init; } = TransferPriority.Normal;

    public IReadOnlyList<TransferSourceSpec> Sources { get; init; } = [];
    public string? ExpectedSha1 { get; init; }

    /// <summary>Full on-disk path of an Http task (ignored by Simulated tasks). Wired up in stage 2.</summary>
    public string? DestPath { get; init; }

    /// <summary>Hidden background task (asset backfill / Java provisioning and similar batch files): not listed in the task center, only aggregated into a summary.</summary>
    public bool Hidden { get; init; }

    /// <summary>Total bytes; -1 means unknown (drives an indeterminate progress bar)</summary>
    public long BytesTotal { get; init; } = -1;

    /// <summary>Simulated rate for Simulated tasks (bytes/second)</summary>
    public double SimulatedSpeed { get; init; } = 1_500_000;
}

/// <summary>Priority convention constants (the business layer may define its own values).</summary>
public static class TransferPriority
{
    public const int CoreGameFile = 50;
    public const int Library = 40;
    public const int AssetIndex = 30;
    public const int Asset = 20;
    public const int Normal = 10;
    public const int Low = 0;
}

/// <summary>
/// Task unit run by the engine: description fields are immutable; runtime fields may only be written by the engine (same assembly);
/// the UI always reads through an immutable TransferSnapshot to avoid cross-thread races.
/// </summary>
public sealed class TransferTask
{
    public string Id { get; } = Guid.NewGuid().ToString("N");
    public long Sequence { get; }
    public TransferSpec Spec { get; }

    internal TransferTask(TransferSpec spec, long sequence)
    {
        Spec = spec;
        Sequence = sequence;
    }

    // —— Engine runtime state (internal set, writable within the same assembly) ——
    public TransferState State { get; internal set; } = TransferState.Queued;
    public long BytesReceived { get; internal set; }
    public double SpeedBytesPerSecond { get; internal set; }
    public string? Error { get; internal set; }
    public int Attempt { get; internal set; } = 1;
    public string CurrentSource { get; internal set; } = "";

    /// <summary>Set by Pause(id); after the worker is interrupted it tells "pause" and "cancel" apart, then resets</summary>
    internal bool PauseRequested { get; set; }

    public bool IsActive => State is TransferState.Queued or TransferState.Downloading or TransferState.Verifying;

    /// <summary>Terminal state: can be cleared from the task panel</summary>
    public bool IsFinished => State is TransferState.Completed or TransferState.Failed or TransferState.Cancelled;
}

/// <summary>Read-only snapshot sent to the UI.</summary>
public sealed record TransferSnapshot(
    string Id,
    string DisplayName,
    TransferState State,
    long BytesTotal,
    long BytesReceived,
    double SpeedBytesPerSecond,
    int Attempt,
    int SourceCount,
    string CurrentSource,
    string? Error,
    int Priority,
    bool Hidden = false)
{
    /// <summary>Whether it should be displayed as "active" (used for sorting)</summary>
    public bool ActiveLike => State is TransferState.Queued or TransferState.Downloading or TransferState.Verifying;
}

/// <summary>Engine parameters (adjustable at runtime).</summary>
public sealed class TransferOptions
{
    private int _maxConcurrency = 6;

    /// <summary>Concurrency limit, default 6, range 2–16</summary>
    public int MaxConcurrency
    {
        get => _maxConcurrency;
        set => _maxConcurrency = Math.Clamp(value, 2, 16);
    }

    /// <summary>Max attempts per source; after that, move on to the next source</summary>
    public int MaxAttemptsPerSource { get; set; } = 3;

    /// <summary>Base retry backoff (milliseconds), growing exponentially with the attempt count</summary>
    public int RetryBaseDelayMs { get; set; } = 500;
}
