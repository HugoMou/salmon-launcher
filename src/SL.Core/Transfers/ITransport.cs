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

/// <summary>
/// Transport implementation interface. In stage 1 SimulatedTransport provides a predictable demo data flow;
/// stage 2 implements HttpTransport (real disk writes + Range resume), and the engine sees no difference.
/// </summary>
public interface ITransport
{
    Task<TransportResult> TransferAsync(TransferContext context, CancellationToken cancellation);
}

/// <summary>Transport context: built by the engine, read-only inside transport.</summary>
public sealed class TransferContext
{
    public required TransferTask Task { get; init; }
    public required string SourceUrl { get; init; }

    /// <summary>Prefix bytes already completed before this attempt (resume support)</summary>
    public required long ResumeBytes { get; init; }

    /// <summary>Which attempt this is (1-based, used to inject failures in demos)</summary>
    public required int AttemptNumber { get; init; }

    /// <summary>transport reports the new cumulative byte count (relative to the start of the file)</summary>
    public required Action<long> Progress { get; init; }
}

public sealed class TransportResult
{
    public required bool Success { get; init; }
    public required long BytesWritten { get; init; }
    public string? FailureReason { get; init; }

    public static TransportResult Ok(long bytes) => new() { Success = true, BytesWritten = bytes };
    public static TransportResult Fail(string reason) => new() { Success = false, BytesWritten = 0, FailureReason = reason };
}
