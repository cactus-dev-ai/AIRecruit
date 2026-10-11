from __future__ import annotations

import asyncio
from types import SimpleNamespace

from app.task_worker.worker import TaskWorker


def test_worker_runs_ten_tasks_concurrently() -> None:
    async def scenario() -> tuple[int, int]:
        worker = TaskWorker.__new__(TaskWorker)
        worker.settings = SimpleNamespace(worker_concurrency=10, worker_poll_seconds=0.01)
        worker._stopping = asyncio.Event()
        active = 0
        peak = 0
        calls = 0

        async def maintenance() -> None:
            return None

        async def run_once() -> bool:
            nonlocal active, peak, calls
            calls += 1
            active += 1
            peak = max(peak, active)
            await asyncio.sleep(0.01)
            active -= 1
            if calls == 10 and active == 0:
                worker._stopping.set()
            return True

        worker.run_maintenance = maintenance
        worker.run_once = run_once
        await worker.run_forever()
        return calls, peak

    calls, peak = asyncio.run(scenario())
    assert calls == 10
    assert peak == 10
