"""Tkinter view for filtering and inspecting Unity logs."""

from collections import OrderedDict
from datetime import datetime, timezone
import tkinter as tk
from tkinter import ttk


class LogsView(ttk.Frame):
    ENTRY_LIMIT = 1000
    ERRORS = {"Error", "Exception", "Assert"}

    def __init__(self, parent):
        super().__init__(parent, padding=10)
        self._entries = OrderedDict()
        self._severity = tk.StringVar(self, "All")
        self._search = tk.StringVar(self)
        self._auto_scroll = tk.BooleanVar(self, True)
        self._status = tk.StringVar(self)

        toolbar = ttk.Frame(self)
        toolbar.pack(fill="x", pady=(0, 6))
        ttk.Label(toolbar, text="Severity").pack(side="left", padx=(0, 6))
        severity = ttk.Combobox(
            toolbar, textvariable=self._severity, values=("All", "Log", "Warning", "Error"),
            state="readonly", width=9,
        )
        severity.pack(side="left", padx=(0, 12))
        ttk.Label(toolbar, text="Search").pack(side="left", padx=(0, 6))
        ttk.Entry(toolbar, textvariable=self._search).pack(side="left", fill="x", expand=True)
        ttk.Checkbutton(
            toolbar, text="Auto-scroll", variable=self._auto_scroll, command=self._scroll_to_latest,
        ).pack(side="left", padx=10)
        ttk.Button(toolbar, text="Clear", command=self.clear).pack(side="left")
        ttk.Label(self, textvariable=self._status).pack(anchor="w", pady=(0, 6))

        panes = ttk.Panedwindow(self, orient="vertical")
        panes.pack(fill="both", expand=True)
        list_frame = ttk.Frame(panes)
        panes.add(list_frame, weight=3)
        self._tree = ttk.Treeview(
            list_frame, columns=("time", "type", "message"), show="headings", selectmode="browse",
        )
        self._tree.heading("time", text="Time")
        self._tree.column("time", width=90, stretch=False)
        self._tree.heading("type", text="Type")
        self._tree.column("type", width=95, stretch=False)
        self._tree.heading("message", text="Message")
        list_scroll = ttk.Scrollbar(list_frame, command=self._tree.yview)
        self._tree.configure(yscrollcommand=list_scroll.set)
        list_scroll.pack(side="right", fill="y")
        self._tree.pack(fill="both", expand=True)
        self._tree.bind("<<TreeviewSelect>>", self._show_details)

        detail_frame = ttk.Frame(panes, padding=(0, 6, 0, 0))
        panes.add(detail_frame, weight=1)
        ttk.Label(detail_frame, text="Details").pack(anchor="w", pady=(0, 4))
        self._details = tk.Text(
            detail_frame, height=6, wrap="word", font="TkFixedFont", state="disabled",
            padx=6, pady=4,
        )
        detail_scroll = ttk.Scrollbar(detail_frame, command=self._details.yview)
        self._details.configure(yscrollcommand=detail_scroll.set)
        self._details.tag_configure("heading", font="TkHeadingFont")
        detail_scroll.pack(side="right", fill="y")
        self._details.pack(fill="both", expand=True)

        self._severity.trace_add("write", self._refresh)
        self._search.trace_add("write", self._refresh)

    def clear(self):
        self._entries.clear()
        self._refresh()

    def append(self, entries):
        severity = self._severity.get()
        search = self._search.get().strip().casefold()
        for entry in entries:
            row = str(entry["Id"])
            if row not in self._entries:
                self._entries[row] = entry
                self._insert_row(row, entry, severity, search)

        while len(self._entries) > self.ENTRY_LIMIT:
            row, _ = self._entries.popitem(last=False)
            if self._tree.exists(row):
                self._tree.delete(row)
        self._scroll_to_latest()

    def set_status(self, message):
        self._status.set(message)

    def _refresh(self, *_):
        selection = self._tree.selection()
        self._tree.delete(*self._tree.get_children())
        severity = self._severity.get()
        search = self._search.get().strip().casefold()
        for row, entry in self._entries.items():
            self._insert_row(row, entry, severity, search)
        if selection and self._tree.exists(selection[0]):
            self._tree.selection_set(selection[0])
            self._tree.focus(selection[0])
        self._show_details()
        self._scroll_to_latest()

    def _insert_row(self, row, entry, severity, search):
        log_type = entry.get("Type") or ""
        level = "Error" if log_type in self.ERRORS else log_type
        if severity != "All" and severity != level:
            return
        message = entry["Message"]
        stack = entry.get("StackTrace") or ""
        if search and search not in message.casefold() and search not in stack.casefold():
            return

        preview = message.partition("\n")[0].partition("\r")[0]
        time = self._format_time(entry.get("Timestamp") or "")
        self._tree.insert("", "end", iid=row, values=(time, log_type, preview))

    def _scroll_to_latest(self):
        if self._auto_scroll.get():
            rows = self._tree.get_children()
            if rows:
                self._tree.see(rows[-1])

    def _show_details(self, _event=None):
        selection = self._tree.selection()
        entry = self._entries.get(selection[0]) if selection else None
        self._details.configure(state="normal")
        self._details.delete("1.0", "end")
        if entry is not None:
            self._details.insert("end", entry["Message"])
            self._details.insert("end", "\n\nStack trace\n", "heading")
            self._details.insert("end", entry.get("StackTrace") or "No stack trace.")
        self._details.configure(state="disabled")

    @staticmethod
    def _format_time(timestamp):
        # Unity sends UTC timestamps with seven fractional digits.
        try:
            time = datetime.strptime(timestamp[:19], "%Y-%m-%dT%H:%M:%S")
        except ValueError:
            return timestamp
        return time.replace(tzinfo=timezone.utc).astimezone().strftime("%H:%M:%S")
