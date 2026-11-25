"""Standalone desktop client for the GameMaster Console Unity server."""

import argparse
import json
from functools import partial
from queue import Empty, Queue
from threading import Thread
from urllib.parse import urlsplit

import tkinter as tk
from tkinter import ttk
from tkinter.scrolledtext import ScrolledText

import requests

from gamemaster_client import DEFAULT_SERVER_URL, post_command
from gamemaster_logs_view import LogsView


def server_address(value):
    value = value.strip()
    if "://" not in value:
        value = f"http://{value}"
    try:
        url = urlsplit(value)
        if (
            url.scheme in ("http", "https")
            and url.hostname
            and not any(char.isspace() for char in url.netloc)
            and url.port != 0
            and url.username is None
            and url.path in ("", "/")
            and not url.query
            and not url.fragment
        ):
            return f"{url.scheme}://{url.netloc}"
    except ValueError:
        pass
    raise argparse.ArgumentTypeError(f"Enter a server address such as {DEFAULT_SERVER_URL}.")


def parse_command(line):
    words = []
    word = []
    quote = None
    started = False
    characters = iter(enumerate(line))
    for index, char in characters:
        if quote:
            if char == quote:
                quote = None
            elif quote == '"' and char == "\\" and line[index + 1:index + 2] in ('"', "\\"):
                word.append(next(characters)[1])
            else:
                word.append(char)
        elif char.isspace():
            if started:
                words.append("".join(word))
                word.clear()
                started = False
        else:
            if not started and char in ("'", '"'):
                quote = char
            else:
                word.append(char)
            started = True
    if quote:
        raise ValueError("Close the quote before running the command.")
    if started:
        words.append("".join(word))
    if not words or not words[0].strip():
        raise ValueError("Enter a command.")
    return words[0], words[1:]


def parse_help(message):
    descriptions = {}
    name = None
    for line in message.splitlines():
        heading, separator, description = line.partition(" - ")
        if separator and heading and not heading[0].isspace():
            name = heading.rstrip()
            descriptions[name] = [description]
        elif name:
            descriptions[name].append(line)
    return {
        name: "\n".join(descriptions[name]).strip()
        for name in sorted(descriptions, key=str.casefold)
    }


def read_json(response):
    response.raise_for_status()
    try:
        return response.json()
    except ValueError:
        raise ValueError("The server returned invalid JSON.") from None


def read_command_result(address, command, args):
    result = read_json(post_command(address, command, args))
    if not isinstance(result, dict) or type(result.get("Success")) is not bool:
        raise ValueError("The server returned an invalid command response.")
    success = result["Success"]
    message = result.get("Message")
    if not isinstance(message, str) or not message:
        message = "Command executed successfully." if success else "Command failed."
    data = result.get("Data")
    details = {}
    if isinstance(data, dict):
        for key, value in data.items():
            if key not in ("command", "arguments", "timestamp", "status"):
                details[key] = value if isinstance(value, str) else json.dumps(value, ensure_ascii=False)
    return success, message, details


def read_log_page(address, session, cursor):
    params = {"after": cursor, "limit": 200}
    if session:
        params["session"] = session
    response = requests.get(f"{address}/logs", params=params, timeout=10)
    if response.status_code == 404:
        return None
    page = read_json(response)
    if not isinstance(page, dict):
        raise ValueError("The server returned an invalid log response.")
    page_session = page.get("SessionId")
    entries = page.get("Entries")
    more = page.get("HasMore")
    if (
        not isinstance(page_session, str) or not page_session.strip()
        or not isinstance(entries, list) or len(entries) > 200
        or type(more) is not bool or (more and not entries)
    ):
        raise ValueError("The server returned an invalid log response.")
    last_id = cursor if session == page_session else 0
    for entry in entries:
        if (
            not isinstance(entry, dict) or type(entry.get("Id")) is not int
            or entry["Id"] <= last_id or not isinstance(entry.get("Message"), str)
            or any(entry.get(key) is not None and not isinstance(entry[key], str)
                   for key in ("Timestamp", "Type", "StackTrace"))
        ):
            raise ValueError("The server returned an invalid log entry.")
        last_id = entry["Id"]
    return page_session, entries, more


class GameMasterApp:
    def __init__(self, root, address=DEFAULT_SERVER_URL):
        self.root = root
        self.address = address
        self.address_input = tk.StringVar(value=address)
        self.status = tk.StringVar(value="Disconnected")
        self.command_input = tk.StringVar()
        self.help_status = tk.StringVar(value="Connect to load commands.")
        self.history = []
        self.history_index = 0
        self.draft = ""
        self.descriptions = {}
        self.suggestions = []
        self.results = Queue()
        self.busy = False
        self.log_generation = 0
        self.log_timer = None
        self.log_pending = False
        self.log_session = None
        self.log_cursor = 0
        self.logs_supported = True

        root.title("GameMaster Console")
        root.geometry("900x625")
        root.minsize(640, 440)
        self.build_window()
        self.logs.set_status("Waiting for Unity logs…")
        root.after(50, self.read_results)
        root.after_idle(self.connect)

    def build_window(self):
        layout = ttk.Frame(self.root, padding=14)
        layout.pack(fill="both", expand=True)
        server = ttk.Frame(layout)
        server.pack(fill="x", pady=(0, 12))
        ttk.Label(server, text="Server", width=8).pack(side="left")
        self.address_entry = ttk.Entry(server, textvariable=self.address_input)
        self.address_entry.pack(side="left", fill="x", expand=True, padx=(0, 8))
        self.address_entry.bind("<Return>", lambda event: self.connect())
        self.connect_button = ttk.Button(server, text="Connect", command=self.connect)
        self.connect_button.pack(side="left")
        ttk.Label(server, textvariable=self.status, width=15, anchor="e").pack(side="left", padx=(8, 0))

        self.tabs = ttk.Notebook(layout)
        self.tabs.pack(fill="both", expand=True)
        self.console = ttk.Frame(self.tabs, padding=10)
        self.help = ttk.Frame(self.tabs, padding=10)
        self.logs = LogsView(self.tabs)
        self.tabs.add(self.console, text="Console")
        self.tabs.add(self.help, text="Help")
        self.tabs.add(self.logs, text="Logs")
        self.tabs.enable_traversal()
        self.tabs.bind("<<NotebookTabChanged>>", self.tab_changed)

        self.output = ScrolledText(self.console, state="disabled", wrap="word", font="TkFixedFont", padx=8, pady=7)
        self.output.tag_configure("command", font=("TkFixedFont", 11, "bold"))
        self.output.tag_configure("error", underline=True)
        self.output.pack(fill="both", expand=True)
        self.suggestion_row = ttk.Frame(self.console)
        self.suggestion_row.pack(fill="x", pady=(6, 0))
        command_row = ttk.Frame(self.console)
        command_row.pack(fill="x", pady=(6, 0))
        ttk.Label(command_row, text="Command").pack(side="left", padx=(0, 8))
        self.command_entry = ttk.Entry(command_row, textvariable=self.command_input, font="TkFixedFont")
        self.command_entry.pack(side="left", fill="x", expand=True, padx=(0, 8))
        self.command_entry.bind("<Return>", self.run_command)
        self.command_entry.bind("<Up>", partial(self.recall, -1))
        self.command_entry.bind("<Down>", partial(self.recall, 1))
        self.command_entry.bind("<Tab>", self.complete)
        self.command_input.trace_add("write", self.update_suggestions)
        self.run_button = ttk.Button(command_row, text="Run", command=self.run_command)
        self.run_button.pack(side="left")
        self.recent_row = ttk.Frame(self.console)
        ttk.Label(self.recent_row, text="Recent").pack(side="left", anchor="n", padx=(0, 8))
        recent_scroller = ttk.Frame(self.recent_row)
        recent_scroller.pack(side="left", fill="x", expand=True)
        background = ttk.Style(self.root).lookup("TFrame", "background") or self.root.cget("background")
        self.recent_canvas = tk.Canvas(
            recent_scroller, borderwidth=0, highlightthickness=0, takefocus=False, background=background,
        )
        self.recent_canvas.pack(fill="x")
        self.recent_buttons = ttk.Frame(self.recent_canvas)
        self.recent_canvas.create_window(0, 0, window=self.recent_buttons, anchor="nw")
        self.recent_buttons.bind("<Configure>", self.resize_recent)
        recent_scrollbar = ttk.Scrollbar(recent_scroller, orient="horizontal", command=self.recent_canvas.xview)
        self.recent_canvas.configure(xscrollcommand=recent_scrollbar.set)
        recent_scrollbar.pack(fill="x")
        self.command_entry.focus_set()

        help_header = ttk.Frame(self.help)
        help_header.pack(fill="x", pady=(0, 8))
        ttk.Label(help_header, textvariable=self.help_status).pack(side="left")
        self.refresh_button = ttk.Button(help_header, text="Refresh", command=self.connect)
        self.refresh_button.pack(side="right")
        self.use_button = ttk.Button(help_header, text="Use Command", command=self.use_help, state="disabled")
        self.use_button.pack(side="right", padx=(0, 8))
        help_list = ttk.Frame(self.help)
        help_list.pack(fill="both", expand=True)
        self.commands = ttk.Treeview(
            help_list, columns=("description",), selectmode="browse", show=("tree", "headings"),
        )
        self.commands.heading("#0", text="Command")
        self.commands.heading("description", text="Description")
        self.commands.column("#0", width=235, minwidth=120, stretch=False)
        self.commands.column("description", width=480, minwidth=180)
        help_scroll = ttk.Scrollbar(help_list, command=self.commands.yview)
        self.commands.configure(yscrollcommand=help_scroll.set)
        help_scroll.pack(side="right", fill="y")
        self.commands.pack(side="left", fill="both", expand=True)
        self.commands.bind("<<TreeviewSelect>>", self.show_description)
        self.commands.bind("<Double-Button-1>", self.use_help)
        self.commands.bind("<Return>", self.use_help)
        self.description = ScrolledText(self.help, state="disabled", wrap="word", height=4, padx=8, pady=7)
        self.description.pack(fill="x", pady=(8, 0))

    def submit(self, operation, callback):
        def run():
            try:
                result = operation()
            except requests.HTTPError as exc:
                error = f"Server returned HTTP {exc.response.status_code}."
                if exc.response.text.strip():
                    error += f"\n{exc.response.text.strip()}"
                self.results.put((callback, None, error))
            except requests.Timeout:
                self.results.put((callback, None, "The server request timed out."))
            except requests.RequestException:
                self.results.put((callback, None, "Could not reach the server."))
            except ValueError as exc:
                self.results.put((callback, None, str(exc)))
            else:
                self.results.put((callback, result, None))

        Thread(target=run, daemon=True).start()

    def read_results(self):
        while True:
            try:
                callback, result, error = self.results.get_nowait()
            except Empty:
                break
            callback(result, error)
        self.root.after(50, self.read_results)

    def apply_address(self):
        try:
            address = server_address(self.address_input.get())
        except argparse.ArgumentTypeError as exc:
            self.status.set("Invalid address")
            self.help_status.set(str(exc))
            if self.tabs.select() == str(self.logs):
                self.reset_logs()
                self.logs.set_status(str(exc))
            else:
                self.write_output(str(exc), "error")
            return False
        self.address_input.set(address)
        if address != self.address:
            self.address = address
            self.set_help("")
            self.reset_logs()
        return True

    def connect(self):
        if self.busy or not self.apply_address():
            return
        self.status.set("Connecting…")
        self.help_status.set("Loading commands…")
        self.set_busy(True)
        self.submit(partial(read_command_result, self.address, "help", []), self.connected)

    def connected(self, result, error):
        self.set_busy(False)
        self.status.set("Disconnected" if error else "Connected")
        if error or not result[0]:
            self.help_status.set(error or result[1])
            self.write_output(error or result[1], "error")
            return
        self.set_help(result[1])
        self.logs_supported = True
        self.schedule_logs(0)

    def set_help(self, message):
        self.descriptions = parse_help(message)
        self.help_status.set("Select a command to see its details." if self.descriptions else "No command descriptions.")
        self.commands.delete(*self.commands.get_children())
        for name, description in self.descriptions.items():
            first_line = description.partition("\n")[0]
            self.commands.insert("", "end", text=name, values=(first_line,))
        self.show_description()
        self.update_suggestions()

    def show_description(self, event=None):
        selected = self.commands.selection()
        self.use_button.configure(state="normal" if selected else "disabled")
        name = self.commands.item(selected[0], "text") if selected else ""
        self.description.configure(state="normal")
        self.description.delete("1.0", "end")
        self.description.insert("end", self.descriptions.get(name, ""))
        self.description.configure(state="disabled")

    def use_help(self, event=None):
        selected = self.commands.selection()
        if selected:
            self.tabs.select(self.console)
            self.fill_command(self.commands.item(selected[0], "text"))

    def run_command(self, event=None, *, line=None):
        if self.busy:
            return "break"
        typed = line is None
        line = (self.command_input.get() if typed else line).strip()
        if not line:
            return "break"
        try:
            command, args = parse_command(line)
        except ValueError as exc:
            self.write_output(str(exc), "error")
            return "break"
        if not self.apply_address():
            return "break"
        if line in self.history:
            self.history.remove(line)
        self.history.append(line)
        self.history_index = len(self.history)
        self.update_recent()
        if typed:
            self.command_input.set("")
        self.command_entry.focus_set()
        self.write_output(f"> {line}", "command")
        self.set_busy(True)
        self.status.set("Running…")
        self.submit(partial(read_command_result, self.address, command, args),
                    partial(self.command_finished, command))
        return "break"

    def command_finished(self, command, result, error):
        self.set_busy(False)
        self.status.set("Disconnected" if error else "Connected")
        if error:
            self.write_output(error, "error")
            return
        success, message, details = result
        text = "\n".join([message, *(f"{key}: {value}" for key, value in details.items())])
        self.write_output(text, "result" if success else "error")
        if success and command.casefold() == "help":
            self.set_help(message)
        elif success and not self.descriptions:
            self.connect()

    def set_busy(self, busy):
        self.busy = busy
        state = "disabled" if busy else "normal"
        for widget in (self.address_entry, self.connect_button, self.run_button, self.refresh_button):
            widget.configure(state=state)
        for button in self.recent_buttons.winfo_children():
            button.configure(state=state)

    def write_output(self, message, kind):
        self.output.configure(state="normal")
        self.output.insert("end", message + "\n", kind)
        self.output.configure(state="disabled")
        self.output.see("end")

    def update_recent(self):
        if not self.recent_row.winfo_manager():
            self.recent_row.pack(fill="x", pady=(8, 0))
        for button in self.recent_buttons.winfo_children():
            button.destroy()
        for line in reversed(self.history[-10:]):
            button = ttk.Button(self.recent_buttons, text=line, command=partial(self.run_command, line=line))
            button.pack(side="left", padx=(0, 4))
            button.bind("<FocusIn>", self.show_recent_button)
        self.recent_canvas.xview_moveto(0)

    def resize_recent(self, event):
        self.recent_canvas.configure(height=event.height, scrollregion=(0, 0, event.width, event.height))

    def show_recent_button(self, event):
        canvas = self.recent_canvas
        button = event.widget
        x = button.winfo_x()
        right = x + button.winfo_width()
        left_edge = canvas.canvasx(0)
        width = canvas.winfo_width()
        if x < left_edge or right > left_edge + width:
            target = min(x, right - width)
            canvas.xview_moveto(target / self.recent_buttons.winfo_width())

    def recall(self, step, event):
        index = self.history_index + step
        if 0 <= index <= len(self.history):
            if self.history_index == len(self.history):
                self.draft = self.command_input.get()
            self.history_index = index
            self.command_input.set(self.history[index] if index < len(self.history) else self.draft)
            self.command_entry.icursor("end")
        return "break"

    def update_suggestions(self, *args):
        prefix = self.command_input.get().lstrip()
        self.suggestions = []
        if prefix and not any(char.isspace() for char in prefix):
            self.suggestions = [
                name for name in self.descriptions if name.casefold().startswith(prefix.casefold())
            ][:6]
        for widget in self.suggestion_row.winfo_children():
            widget.destroy()
        for name in self.suggestions:
            ttk.Button(self.suggestion_row, text=name, command=partial(self.fill_command, name)).pack(side="left")

    def fill_command(self, name):
        self.history_index = len(self.history)
        self.command_input.set(name + " ")
        self.command_entry.focus_set()
        self.command_entry.icursor("end")

    def complete(self, event):
        if self.suggestions:
            self.fill_command(self.suggestions[0])
            return "break"

    def tab_changed(self, event):
        selected = self.tabs.select()
        if selected == str(self.logs):
            if self.apply_address():
                self.schedule_logs(0)
        else:
            self.cancel_log_timer()
            if selected == str(self.help) and not self.descriptions:
                self.connect()
            elif selected == str(self.console):
                self.command_entry.focus_set()

    def reset_logs(self):
        self.cancel_log_timer()
        self.log_generation += 1
        self.log_pending = False
        self.log_session = None
        self.log_cursor = 0
        self.logs_supported = True
        self.logs.clear()
        self.logs.set_status("Waiting for Unity logs…")

    def cancel_log_timer(self):
        if self.log_timer is not None:
            self.root.after_cancel(self.log_timer)
            self.log_timer = None

    def schedule_logs(self, delay):
        if (
            self.tabs.select() == str(self.logs) and self.log_timer is None
            and self.logs_supported and not self.log_pending
        ):
            self.log_timer = self.root.after(delay, self.poll_logs)

    def poll_logs(self):
        self.log_timer = None
        self.log_pending = True
        self.submit(partial(read_log_page, self.address, self.log_session, self.log_cursor),
                    partial(self.logs_received, self.log_generation))

    def logs_received(self, generation, result, error):
        if generation != self.log_generation:
            return
        self.log_pending = False
        if error:
            self.logs.set_status(error)
            self.schedule_logs(3000)
            return
        if result is None:
            self.logs_supported = False
            self.logs.set_status("This server does not provide Unity logs.")
            return
        session, entries, more = result
        if session != self.log_session:
            self.log_session = session
            self.log_cursor = 0
            self.logs.clear()
        if entries:
            self.logs.append(entries)
            self.log_cursor = entries[-1]["Id"]
        self.logs.set_status("Live" if self.log_cursor else "Waiting for Unity logs…")
        self.schedule_logs(0 if more else 1000)


def main():
    parser = argparse.ArgumentParser(description="Open the standalone GameMaster Console desktop app.")
    parser.add_argument("--server", type=server_address, default=DEFAULT_SERVER_URL, help="GameMaster server address")
    options = parser.parse_args()
    root = tk.Tk()
    GameMasterApp(root, options.server)
    root.mainloop()


if __name__ == "__main__":
    main()
