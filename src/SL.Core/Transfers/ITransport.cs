// Salmon Launcher (SL) — a Minecraft Java edition launcher
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
/// 传输实现接口。阶段一由 SimulatedTransport 提供可预期的演示数据流;
/// 阶段二实现 HttpTransport(真实写盘 + Range 续传),引擎不感知差别。
/// </summary>
public interface ITransport
{
    Task<TransportResult> TransferAsync(TransferContext context, CancellationToken cancellation);
}

/// <summary>传输上下文:引擎构造,transport 内部只读。</summary>
public sealed class TransferContext
{
    public required TransferTask Task { get; init; }
    public required string SourceUrl { get; init; }

    /// <summary>本次尝试前已完成的前缀字节数(断点续传)</summary>
    public required long ResumeBytes { get; init; }

    /// <summary>当前是第几次尝试(从 1 开始,用于演示注入失败)</summary>
    public required int AttemptNumber { get; init; }

    /// <summary>transport 汇报新的累计字节数(相对文件开头)</summary>
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
