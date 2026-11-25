"""Shared wire format for the GameMaster Console clients."""

DEFAULT_SERVER_URL = "http://localhost:54345"


def command_payload(command, args):
    return {
        "Command": command,
        "Arguments": {str(index): value for index, value in enumerate(args)},
    }
