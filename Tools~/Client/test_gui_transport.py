"""Exercise the standalone GUI's HTTP transport without opening a window."""

import json
from functools import partial
from http.server import BaseHTTPRequestHandler, HTTPServer
from queue import Queue
from threading import Thread
import unittest
from unittest.mock import patch
from urllib.error import URLError
from urllib.parse import parse_qs, urlsplit

from gamemaster_gui import GameMasterApp, NetworkError, read_command_result, read_log_page


class TestServerHandler(BaseHTTPRequestHandler):
    def log_message(self, format, *args):
        pass

    def send_json(self, status, data):
        self.send_response(status)
        self.send_header("Content-Type", "application/json")
        self.end_headers()
        self.wfile.write(json.dumps(data, ensure_ascii=False).encode("utf-8"))

    def do_POST(self):
        if self.path != "/command" or self.headers.get_content_type() != "application/json":
            self.send_error(400, "Expected a JSON command")
            return
        body = json.loads(self.rfile.read(int(self.headers["Content-Length"])))
        if body["Command"] == "Denied":
            self.send_json(403, {"Message": "Access denied."})
            return
        if body["Command"] == "Malformed":
            self.send_response(200)
            self.end_headers()
            self.wfile.write(b"{")
            return
        success = body["Command"] != "Unknown"
        self.send_json(200, {
            "Success": success,
            "Message": "Déplacé" if success else "Command not registered.",
            "Data": {"command": body["Command"], "received": body["Arguments"]},
        })

    def do_GET(self):
        url = urlsplit(self.path)
        if url.path != "/logs":
            self.send_error(400, "Expected the logs endpoint")
            return
        params = parse_qs(url.query)
        if params == {"after": ["0"], "limit": ["200"]}:
            self.send_error(404)
            return
        if params != {"after": ["42"], "limit": ["200"], "session": ["previous/+"]}:
            self.send_error(400, "Expected the log cursor and session")
            return
        self.send_json(200, {
            "SessionId": "new-session",
            "Entries": [{"Id": 1, "Message": "Restarted"}],
            "HasMore": False,
        })


class DesktopTransportTest(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.http = HTTPServer(("127.0.0.1", 0), TestServerHandler)
        cls.address = f"http://127.0.0.1:{cls.http.server_port}"
        cls.worker = Thread(target=cls.http.serve_forever, daemon=True)
        cls.worker.start()

    @classmethod
    def tearDownClass(cls):
        cls.http.shutdown()
        cls.worker.join()
        cls.http.server_close()

    def test_command_posts_shared_payload_and_reads_unicode_result(self):
        success, message, details = read_command_result(self.address, "Move", ["two words", "雪"])
        self.assertTrue(success)
        self.assertEqual(message, "Déplacé")
        self.assertEqual(set(details), {"received"})
        self.assertEqual(json.loads(details["received"]), {"0": "two words", "1": "雪"})

    def test_unregistered_command_is_an_ordinary_response(self):
        self.assertEqual(
            read_command_result(self.address, "Unknown", []),
            (False, "Command not registered.", {"received": "{}"}),
        )

    def test_log_session_reset_and_legacy_server(self):
        self.assertEqual(
            read_log_page(self.address, "previous/+", 42),
            ("new-session", [{"Id": 1, "Message": "Restarted"}], False),
        )
        self.assertIsNone(read_log_page(self.address, None, 0))

    def test_http_and_json_errors_in_worker_are_not_retryable(self):
        self.check_worker_error("Denied", 'Server returned HTTP 403.\n{"Message": "Access denied."}')
        self.check_worker_error("Malformed", "The server returned invalid JSON.")

    def test_worker_identifies_network_timeout(self):
        with patch("gamemaster_gui.urlopen", side_effect=URLError(TimeoutError())):
            self.check_worker_error("Move", NetworkError("The server request timed out."))

    def check_worker_error(self, name, expected):
        window = GameMasterApp.__new__(GameMasterApp)
        window.results = Queue()
        callback = object()
        window.submit(partial(read_command_result, self.address, name, []), callback)
        done, result, error = window.results.get(timeout=3)
        self.assertIs(done, callback)
        self.assertIsNone(result)
        self.assertIs(type(error), type(expected))
        self.assertEqual(error, expected)


if __name__ == "__main__":
    unittest.main()
