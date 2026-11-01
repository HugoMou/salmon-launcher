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

public interface ITransferService
{
    /// <summary>引擎参数(并发数等,可运行时调整)</summary>
    TransferOptions Options { get; }

    /// <summary>提交任务并立即返回任务对象(含 Id)</summary>
    TransferTask Submit(TransferSpec spec);

    void Pause(string id);
    void Resume(string id);
    void Cancel(string id);

    /// <summary>重试:Failed / Cancelled 任务重新入队(进度归零)</summary>
    void Retry(string id);

    void PauseAll();
    void ResumeAll();

    /// <summary>清除所有终态任务(完成/失败/取消)</summary>
    void ClearFinished();

    /// <summary>当前全部任务的快照(UI 初始化时使用)</summary>
    IReadOnlyList<TransferSnapshot> Snapshots { get; }

    /// <summary>单个任务的进度/状态更新(UI 需自行切换到 UI 线程)</summary>
    event Action<TransferSnapshot>? Updated;

    /// <summary>任务集合结构变化(新增/移除),UI 需重建列表</summary>
    event Action? StructureChanged;

    /// <summary>是否有正在执行或排队中的任务</summary>
    bool HasActive { get; }
}
