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

public interface ITransferService
{
    /// <summary>Engine parameters (concurrency, etc.; adjustable at runtime)</summary>
    TransferOptions Options { get; }

    /// <summary>Submit a task and return the task object immediately (including its Id)</summary>
    TransferTask Submit(TransferSpec spec);

    void Pause(string id);
    void Resume(string id);
    void Cancel(string id);

    /// <summary>Retry: re-queue Failed / Cancelled tasks (progress reset to zero)</summary>
    void Retry(string id);

    void PauseAll();
    void ResumeAll();

    /// <summary>Clear all terminal tasks (completed/failed/cancelled)</summary>
    void ClearFinished();

    /// <summary>Snapshot of all current tasks (used when the UI initializes)</summary>
    IReadOnlyList<TransferSnapshot> Snapshots { get; }

    /// <summary>Progress/state update of a single task (the UI must switch to the UI thread itself)</summary>
    event Action<TransferSnapshot>? Updated;

    /// <summary>Task collection structure changed (added/removed); the UI must rebuild its list</summary>
    event Action? StructureChanged;

    /// <summary>Whether any task is running or queued</summary>
    bool HasActive { get; }
}
