#!/usr/bin/env python3
"""SQLite fixtures for the Windows package smoke test."""

import json
import re
import sqlite3
import subprocess
import sys
from contextlib import closing
from pathlib import Path


DATABASE = "muses-youtube-native.sqlite"
TRACK_ID = "11000000-0000-0000-0000-000000000001"
QUEUE_ID = "00000000-0000-0000-0000-000000000001"
ITEM_IDS = (
    "22000000-0000-0000-0000-000000000002",
    "22000000-0000-0000-0000-000000000001",
)
TITLE = "Package smoke track"
ARTIST = "Muses QA"
YOUTUBE_ID = "release-test"
LEGACY_REVISION = "6fabd52c9aae5d5b3b39a8035e05fa834925b995"
LEGACY_ADDITIVE_COLUMNS = (
    ("tracks", "library_member"),
    ("queue_state", "inserted_current_json"),
    ("queue_state", "original_order_json"),
)
PLAYLIST_ID = "33000000-0000-0000-0000-000000000001"
HISTORY_ID = "44000000-0000-0000-0000-000000000001"
NOTE_ID = "55000000-0000-0000-0000-000000000001"
BOOKMARK_ID = "66000000-0000-0000-0000-000000000001"


def check(condition, message):
    if not condition:
        raise RuntimeError(message)


def prepare(database):
    check(not database.exists(), "The isolated test database already exists")
    database.parent.mkdir()
    with closing(sqlite3.connect(database)) as connection, connection:
        # Let the harness close the window through the normal desktop exit path.
        connection.executescript("""
            CREATE TABLE preferences (
                key TEXT PRIMARY KEY,
                value_kind TEXT NOT NULL,
                value_text TEXT,
                value_num REAL,
                value_bool INTEGER
            );
            INSERT INTO preferences (key, value_kind, value_bool)
                VALUES ('muses.desktop.closeToTray', 'bool', 0);
        """)


def seed(connection):
    initial = connection.execute(
        "SELECT items_json, saved_at FROM queue_state WHERE id = ?", (QUEUE_ID,)
    ).fetchone()
    check(initial is not None and json.loads(initial[0]) == [] and initial[1] > 1,
          "The first installed launch did not write an empty queue on exit")

    track = {
        "id": TRACK_ID,
        "title": TITLE,
        "artist": ARTIST,
        "durationSeconds": 30,
        "youTubeId": YOUTUBE_ID,
        "liked": True,
    }
    items = [
        {"id": item_id, "track": track, "fromContext": "songs",
         "queuedAt": "2026-01-01T00:00:00+00:00"}
        for item_id in ITEM_IDS
    ]
    with connection:
        connection.execute("""
            INSERT INTO tracks (
                id, title, artist, youtube_id, metadata_status_raw,
                availability_raw, added_at, duration_ms, liked
            ) VALUES (?, ?, ?, ?, 'embedded', 'available', 1, 30000, 1)
        """, (TRACK_ID, TITLE, ARTIST, YOUTUBE_ID))
        connection.execute("""
            UPDATE queue_state SET items_json = ?, current_index = -1,
                current_track_id = NULL, saved_at = 1 WHERE id = ?
        """, (json.dumps(items), QUEUE_ID))


def verify(connection, restored):
    track = connection.execute(
        "SELECT title, artist, youtube_id, liked FROM tracks WHERE id = ?", (TRACK_ID,)
    ).fetchone()
    check(track == (TITLE, ARTIST, YOUTUBE_ID, 1), "The library fixture changed")

    state = connection.execute(
        "SELECT items_json, saved_at FROM queue_state WHERE id = ?", (QUEUE_ID,)
    ).fetchone()
    check(state is not None, "The queue fixture disappeared")
    items = [(item["id"], item["track"]["id"]) for item in json.loads(state[0])]
    check(items == [(item_id, TRACK_ID) for item_id in ITEM_IDS],
          "The queue occurrence IDs, track IDs or order changed")
    if restored:
        check(state[1] > 1, "The installed application did not save the restored queue")
        membership = connection.execute(
            "SELECT library_member FROM tracks WHERE id = ?", (TRACK_ID,)
        ).fetchone()
        check(membership == (1,), "The migrated track no longer belongs to the library")


def prepare_historical_roaming(database):
    check(database.parent.is_dir() and not database.exists(), "Expected a fresh historical fixture directory")
    source = subprocess.run(
        ["git", "-C", str(Path(__file__).resolve().parents[1]), "show",
         f"{LEGACY_REVISION}:src/Muses.Persistence/MusesSchema.cs"],
        check=True, capture_output=True, encoding="utf-8",
    ).stdout
    sql = re.search(r'cmd\.CommandText\s*=\s*"""\s*(.*?)\s*""";', source, re.DOTALL)
    check(sql is not None, "The pinned source does not contain the historical SQLite schema")
    additions = re.findall(r'EnsureColumn\(connection, "(\w+)", "(\w+)", "([^"\n]+)"\);', source)
    check(bool(additions), "The pinned source does not contain the historical SQLite column upgrades")

    with closing(sqlite3.connect(database)) as connection, connection:
        connection.executescript(sql[1])
        for table, column, definition in additions:
            if not any(row[1] == column for row in connection.execute(f"PRAGMA table_info({table})")):
                connection.execute(f"ALTER TABLE {table} ADD COLUMN {column} {definition}")
        connection.execute("""
            INSERT INTO preferences (key, value_kind, value_bool)
            VALUES ('muses.desktop.closeToTray', 'bool', 0)
        """)
        connection.execute("""
            INSERT INTO queue_state (id, items_json, current_index, up_next_json, history_json,
                repeat_mode_raw, shuffle, saved_at)
            VALUES (?, '[]', -1, '[]', '[]', 'Off', 0, 2)
        """, (QUEUE_ID,))
        seed(connection)
        connection.execute("""
            INSERT INTO playlists (id, name, created_at, pinned) VALUES (?, 'Historical playlist', 1, 1)
        """, (PLAYLIST_ID,))
        connection.executemany("""
            INSERT INTO playlist_items (id, playlist_id, track_id, item_order) VALUES (?, ?, ?, ?)
        """, [(item_id, PLAYLIST_ID, TRACK_ID, index) for index, item_id in enumerate(ITEM_IDS)])
        connection.execute("""
            INSERT INTO listening_events (id, track_id, started_at, ended_at, outcome_raw, listened_ms)
            VALUES (?, ?, 1, 30001, 'Completed', 30000)
        """, (HISTORY_ID, TRACK_ID))
        connection.execute("""
            INSERT INTO track_notes (id, track_id, body, updated_at) VALUES (?, ?, 'Historical note', 1)
        """, (NOTE_ID, TRACK_ID))
        connection.execute("""
            INSERT INTO track_bookmarks (id, track_id, timestamp_ms, label, title, created_at)
            VALUES (?, ?, 5000, 'Historical bookmark', 'Historical bookmark', 1)
        """, (BOOKMARK_ID, TRACK_ID))


def verify_historical_roaming(connection, restored, rearm):
    verify(connection, restored)
    check(connection.execute("SELECT name, pinned FROM playlists WHERE id = ?", (PLAYLIST_ID,)).fetchone()
          == ("Historical playlist", 1), "The historical pinned playlist changed")
    check(connection.execute("""
        SELECT id, track_id FROM playlist_items WHERE playlist_id = ? ORDER BY item_order
    """, (PLAYLIST_ID,)).fetchall() == [(item_id, TRACK_ID) for item_id in ITEM_IDS],
          "The historical playlist order changed")
    check(connection.execute("""
        SELECT track_id, outcome_raw, listened_ms FROM listening_events WHERE id = ?
    """, (HISTORY_ID,)).fetchone() == (TRACK_ID, "Completed", 30000),
          "The historical listening event changed")
    check(connection.execute("SELECT track_id, body FROM track_notes WHERE id = ?", (NOTE_ID,)).fetchone()
          == (TRACK_ID, "Historical note"), "The historical track note changed")
    check(connection.execute("""
        SELECT track_id, timestamp_ms, label, title FROM track_bookmarks WHERE id = ?
    """, (BOOKMARK_ID,)).fetchone() == (TRACK_ID, 5000, "Historical bookmark", "Historical bookmark"),
          "The historical bookmark changed")

    if not restored:
        for table, column in LEGACY_ADDITIVE_COLUMNS:
            check(not any(row[1] == column for row in connection.execute(f"PRAGMA table_info({table})")),
                  f"The previous package already added the new column {table}.{column}")
        saved_at = connection.execute("SELECT saved_at FROM queue_state WHERE id = ?", (QUEUE_ID,)).fetchone()[0]
        check(saved_at > 1, "The previous app did not persist the queue in default Roaming AppData")
    if rearm:
        with connection:
            connection.execute("UPDATE queue_state SET saved_at = 1 WHERE id = ?", (QUEUE_ID,))


def check_database(action, directory):
    database = directory / DATABASE
    if action == "prepare":
        prepare(database)
        return
    if action == "prepare-historical-roaming":
        prepare_historical_roaming(database)
        return

    check(not list(directory.glob(f"{database.stem}-corrupt-*")),
          "The app backed up the database after a startup failure")
    with closing(sqlite3.connect(database.as_uri() + "?mode=rw", uri=True)) as connection:
        integrity = connection.execute("PRAGMA quick_check").fetchall()
        check(integrity == [("ok",)], f"SQLite quick_check failed: {integrity}")
        if action == "seed":
            seed(connection)
        elif action in {"verify-historical-baseline", "verify-historical-roaming-rearm", "verify-historical-roaming"}:
            verify_historical_roaming(
                connection, restored=action != "verify-historical-baseline",
                rearm=action != "verify-historical-roaming",
            )
        else:
            verify(connection, restored=action == "verify-restored")


if __name__ == "__main__":
    action, value = sys.argv[1:]
    if action in {
        "prepare", "seed", "verify-fixture", "verify-restored",
        "prepare-historical-roaming", "verify-historical-baseline",
        "verify-historical-roaming-rearm", "verify-historical-roaming",
    }:
        check_database(action, Path(value).resolve())
    else:
        sys.exit(f"Unknown smoke action: {action}")
