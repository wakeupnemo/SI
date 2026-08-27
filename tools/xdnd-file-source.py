#!/usr/bin/env python3
"""Expose one local file as a native GTK drag source for Linux smoke tests."""

from __future__ import annotations

import argparse
from pathlib import Path

import gi

gi.require_version("Gdk", "4.0")
gi.require_version("Gio", "2.0")
gi.require_version("Gtk", "4.0")

from gi.repository import Gdk, Gio, Gtk  # noqa: E402


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser()
    parser.add_argument("file", type=Path)
    parser.add_argument("--title", default="SIQuester Xdnd file source")
    return parser.parse_args()


class FileDragApplication(Gtk.Application):
    def __init__(self, file_path: Path, title: str) -> None:
        super().__init__(application_id="org.siquester.XdndFileSource")
        self._file_path = file_path
        self._title = title

    def do_activate(self) -> None:
        window = Gtk.ApplicationWindow(application=self, title=self._title)
        window.set_default_size(240, 120)

        button = Gtk.Button(label=f"Drag {self._file_path.name}")
        button.set_margin_top(24)
        button.set_margin_bottom(24)
        button.set_margin_start(16)
        button.set_margin_end(16)

        drag_source = Gtk.DragSource(actions=Gdk.DragAction.COPY)
        drag_source.connect("prepare", self._prepare_drag)
        drag_source.connect("drag-begin", self._drag_begin)
        drag_source.connect("drag-end", self._drag_end)
        button.add_controller(drag_source)
        window.set_child(button)
        window.present()

    def _prepare_drag(
        self,
        _source: Gtk.DragSource,
        _x: float,
        _y: float,
    ) -> Gdk.ContentProvider:
        file_list = Gdk.FileList.new_from_list([Gio.File.new_for_path(str(self._file_path))])
        print(f"XDND_PREPARE {self._file_path}", flush=True)
        return Gdk.ContentProvider.new_for_value(file_list)

    @staticmethod
    def _drag_begin(_source: Gtk.DragSource, _drag: Gdk.Drag) -> None:
        print("XDND_BEGIN", flush=True)

    @staticmethod
    def _drag_end(_source: Gtk.DragSource, drag: Gdk.Drag, _delete_data: bool) -> None:
        print(f"XDND_END {int(drag.get_selected_action())}", flush=True)


def main() -> int:
    args = parse_args()
    file_path = args.file.resolve(strict=True)

    if not file_path.is_file():
        raise SystemExit(f"Not a regular file: {file_path}")

    return FileDragApplication(file_path, args.title).run([])


if __name__ == "__main__":
    raise SystemExit(main())
