#!/usr/bin/env python3
"""Run a bounded native X11 question-preview lifecycle soak and record /proc metrics."""

from __future__ import annotations

import argparse
import ctypes
import ctypes.util
import json
import os
from pathlib import Path
import subprocess
import sys
import time


WINDOW_TITLE = "SIQuester Cross-Platform"


class X11Controller:
    def __init__(self) -> None:
        x11_name = ctypes.util.find_library("X11")
        xtst_name = ctypes.util.find_library("Xtst")
        if not x11_name or not xtst_name:
            raise RuntimeError("libX11 and libXtst are required")

        self.x11 = ctypes.CDLL(x11_name)
        self.xtst = ctypes.CDLL(xtst_name)
        self.x11.XOpenDisplay.argtypes = [ctypes.c_char_p]
        self.x11.XOpenDisplay.restype = ctypes.c_void_p
        self.x11.XDefaultRootWindow.argtypes = [ctypes.c_void_p]
        self.x11.XDefaultRootWindow.restype = ctypes.c_ulong
        self.x11.XQueryTree.argtypes = [
            ctypes.c_void_p,
            ctypes.c_ulong,
            ctypes.POINTER(ctypes.c_ulong),
            ctypes.POINTER(ctypes.c_ulong),
            ctypes.POINTER(ctypes.POINTER(ctypes.c_ulong)),
            ctypes.POINTER(ctypes.c_uint),
        ]
        self.x11.XFetchName.argtypes = [ctypes.c_void_p, ctypes.c_ulong, ctypes.POINTER(ctypes.c_char_p)]
        self.x11.XFree.argtypes = [ctypes.c_void_p]
        self.x11.XMoveResizeWindow.argtypes = [
            ctypes.c_void_p,
            ctypes.c_ulong,
            ctypes.c_int,
            ctypes.c_int,
            ctypes.c_uint,
            ctypes.c_uint,
        ]
        self.x11.XRaiseWindow.argtypes = [ctypes.c_void_p, ctypes.c_ulong]
        self.x11.XTranslateCoordinates.argtypes = [
            ctypes.c_void_p,
            ctypes.c_ulong,
            ctypes.c_ulong,
            ctypes.c_int,
            ctypes.c_int,
            ctypes.POINTER(ctypes.c_int),
            ctypes.POINTER(ctypes.c_int),
            ctypes.POINTER(ctypes.c_ulong),
        ]
        self.x11.XStringToKeysym.argtypes = [ctypes.c_char_p]
        self.x11.XStringToKeysym.restype = ctypes.c_ulong
        self.x11.XKeysymToKeycode.argtypes = [ctypes.c_void_p, ctypes.c_ulong]
        self.x11.XKeysymToKeycode.restype = ctypes.c_uint
        self.x11.XFlush.argtypes = [ctypes.c_void_p]
        self.xtst.XTestFakeMotionEvent.argtypes = [
            ctypes.c_void_p,
            ctypes.c_int,
            ctypes.c_int,
            ctypes.c_int,
            ctypes.c_ulong,
        ]
        self.xtst.XTestFakeButtonEvent.argtypes = [ctypes.c_void_p, ctypes.c_uint, ctypes.c_int, ctypes.c_ulong]
        self.xtst.XTestFakeKeyEvent.argtypes = [ctypes.c_void_p, ctypes.c_uint, ctypes.c_int, ctypes.c_ulong]

        display_name = os.environ.get("DISPLAY")
        if not display_name:
            raise RuntimeError("DISPLAY must be set")
        self.display = self.x11.XOpenDisplay(display_name.encode())
        if not self.display:
            raise RuntimeError(f"could not open X11 display {display_name}")
        self.root = self.x11.XDefaultRootWindow(self.display)

    def find_window(self, title: str) -> int | None:
        return self._find_window(self.root, title)

    def _find_window(self, parent: int, title: str) -> int | None:
        root_return = ctypes.c_ulong()
        parent_return = ctypes.c_ulong()
        children = ctypes.POINTER(ctypes.c_ulong)()
        count = ctypes.c_uint()
        if not self.x11.XQueryTree(
            self.display,
            parent,
            ctypes.byref(root_return),
            ctypes.byref(parent_return),
            ctypes.byref(children),
            ctypes.byref(count),
        ):
            return None
        try:
            for index in range(count.value):
                window = int(children[index])
                name = ctypes.c_char_p()
                if self.x11.XFetchName(self.display, window, ctypes.byref(name)) and name.value:
                    try:
                        if name.value.decode(errors="replace") == title:
                            return window
                    finally:
                        self.x11.XFree(name)
                found = self._find_window(window, title)
                if found is not None:
                    return found
        finally:
            if children:
                self.x11.XFree(children)
        return None

    def prepare(self, window: int) -> None:
        self.x11.XMoveResizeWindow(self.display, window, 10, 10, 1200, 760)
        self.x11.XRaiseWindow(self.display, window)
        self.x11.XFlush(self.display)

    def click(self, window: int, x: int, y: int) -> None:
        root_x = ctypes.c_int()
        root_y = ctypes.c_int()
        child = ctypes.c_ulong()
        self.x11.XTranslateCoordinates(
            self.display,
            window,
            self.root,
            x,
            y,
            ctypes.byref(root_x),
            ctypes.byref(root_y),
            ctypes.byref(child),
        )
        self.xtst.XTestFakeMotionEvent(self.display, -1, root_x.value, root_y.value, 0)
        self.xtst.XTestFakeButtonEvent(self.display, 1, 1, 0)
        self.xtst.XTestFakeButtonEvent(self.display, 1, 0, 0)
        self.x11.XFlush(self.display)

    def key(self, *names: str) -> None:
        keycodes = []
        for name in names:
            keysym = self.x11.XStringToKeysym(name.encode())
            keycode = self.x11.XKeysymToKeycode(self.display, keysym)
            if keycode == 0:
                raise RuntimeError(f"X11 key is unavailable: {name}")
            keycodes.append(keycode)
        for keycode in keycodes:
            self.xtst.XTestFakeKeyEvent(self.display, keycode, 1, 0)
        for keycode in reversed(keycodes):
            self.xtst.XTestFakeKeyEvent(self.display, keycode, 0, 0)
        self.x11.XFlush(self.display)


def process_tree(root_pid: int) -> list[int]:
    parents: dict[int, int] = {}
    for stat_path in Path("/proc").glob("[0-9]*/stat"):
        try:
            text = stat_path.read_text()
            close_parenthesis = text.rfind(")")
            fields = text[close_parenthesis + 2 :].split()
            parents[int(stat_path.parent.name)] = int(fields[1])
        except (OSError, ValueError, IndexError):
            continue
    result = {root_pid}
    while True:
        descendants = {pid for pid, parent in parents.items() if parent in result}
        expanded = result | descendants
        if expanded == result:
            return sorted(result)
        result = expanded


def process_sample(root_pid: int, cycle: int) -> dict[str, object]:
    pids = process_tree(root_pid)
    rss_kib = 0
    threads = 0
    file_descriptors = 0
    names: list[str] = []
    for pid in pids:
        proc = Path("/proc") / str(pid)
        try:
            status = (proc / "status").read_text().splitlines()
            values = {line.split(":", 1)[0]: line.split(":", 1)[1].strip() for line in status if ":" in line}
            rss_kib += int(values.get("VmRSS", "0 kB").split()[0])
            threads += int(values.get("Threads", "0"))
            file_descriptors += len(list((proc / "fd").iterdir()))
            names.append((proc / "comm").read_text().strip())
        except (OSError, ValueError):
            continue
    webkit_gstreamer = sum(
        1 for name in names if "WebKit" in name or "MiniBrowser" in name or "gst" in name.lower()
    )
    return {
        "cycle": cycle,
        "rss_kib": rss_kib,
        "threads": threads,
        "file_descriptors": file_descriptors,
        "processes": len(pids),
        "webkit_gstreamer_processes": webkit_gstreamer,
        "process_names": sorted(names),
    }


def wait_for_window(controller: X11Controller, process: subprocess.Popen[bytes]) -> int:
    deadline = time.monotonic() + 20
    while time.monotonic() < deadline:
        if process.poll() is not None:
            raise RuntimeError(f"SIQuester exited before opening a window: {process.returncode}")
        window = controller.find_window(WINDOW_TITLE)
        if window is not None:
            return window
        time.sleep(0.2)
    raise RuntimeError("SIQuester did not expose its main window within 20 seconds")


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("application", type=Path)
    parser.add_argument("package", type=Path)
    parser.add_argument("receipt_directory", type=Path)
    parser.add_argument("--cycles", type=int, default=50)
    args = parser.parse_args()
    if args.cycles < 1 or args.cycles > 100:
        parser.error("--cycles must be between 1 and 100")
    args.receipt_directory.mkdir(parents=True, exist_ok=True)
    args.receipt_directory = args.receipt_directory.resolve()
    xdg = args.receipt_directory / "xdg"
    env = os.environ.copy()
    env["XDG_CONFIG_HOME"] = str(xdg / "config")
    env["XDG_STATE_HOME"] = str(xdg / "state")
    env["XDG_CACHE_HOME"] = str(xdg / "cache")
    command = [str(args.application), str(args.package)]
    process = subprocess.Popen(command, env=env)
    controller = X11Controller()
    samples: list[dict[str, object]] = []
    started = time.monotonic()

    try:
        window = wait_for_window(controller, process)
        controller.prepare(window)
        time.sleep(3)
        controller.click(window, 75, 316)
        time.sleep(0.5)
        controller.click(window, 125, 348)
        time.sleep(0.5)

        controller.click(window, 812, 161)
        time.sleep(4)
        controller.click(window, 900, 660)
        time.sleep(1)
        samples.append(process_sample(process.pid, 0))

        latencies_ms: list[float] = []
        for cycle in range(1, args.cycles + 1):
            cycle_started = time.monotonic()
            controller.click(window, 812, 161)
            time.sleep(0.45)
            controller.click(window, 900, 660)
            time.sleep(0.20)
            latencies_ms.append((time.monotonic() - cycle_started) * 1000)
            if cycle % 10 == 0 or cycle == args.cycles:
                time.sleep(1)
                samples.append(process_sample(process.pid, cycle))

        controller.click(window, 75, 316)
        time.sleep(0.2)
        controller.key("Control_L", "q")
        return_code = process.wait(timeout=20)
        if return_code != 0:
            raise RuntimeError(f"SIQuester exited with {return_code}")

        log_path = xdg / "state" / "SIQuester" / "logs" / "siquester.log"
        log_text = log_path.read_text(errors="replace")
        fatal_lines = [
            line for line in log_text.splitlines()
            if "|FATAL|" in line or "Unhandled exception" in line or "Question preview host failed" in line
        ]
        created = log_text.count("Question preview media session created")
        disposed = log_text.count("Question preview media session disposed")
        if fatal_lines:
            raise RuntimeError(f"fatal/unhandled preview log entries: {len(fatal_lines)}")
        if created != disposed or created < args.cycles:
            raise RuntimeError(f"preview session ownership mismatch: created={created}, disposed={disposed}")

        ordered = sorted(latencies_ms)
        receipt = {
            "application": str(args.application.resolve()),
            "package": str(args.package.resolve()),
            "cycles": args.cycles,
            "elapsed_seconds": round(time.monotonic() - started, 3),
            "latency_ms": {
                "p50": round(ordered[len(ordered) // 2], 3),
                "p95": round(ordered[min(len(ordered) - 1, int(len(ordered) * 0.95))], 3),
                "max": round(ordered[-1], 3),
            },
            "preview_sessions_created": created,
            "preview_sessions_disposed": disposed,
            "fatal_or_unhandled_lines": len(fatal_lines),
            "samples": samples,
            "log_path": str(log_path),
        }
        receipt_path = args.receipt_directory / "preview-soak.json"
        receipt_path.write_text(json.dumps(receipt, ensure_ascii=False, indent=2) + "\n")
        print(json.dumps(receipt, ensure_ascii=False, indent=2))
        return 0
    finally:
        if process.poll() is None:
            process.terminate()
            try:
                process.wait(timeout=5)
            except subprocess.TimeoutExpired:
                process.kill()
                process.wait()


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except Exception as exception:
        print(f"preview soak failed: {exception}", file=sys.stderr)
        raise
