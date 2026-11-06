#!/usr/bin/env bash
set -Eeuo pipefail

fail() {
    printf '%s\n' "$*" >&2
    exit 1
}

if (( $# < 1 || $# > 2 )); then
    fail "Usage: bash scripts/test-deb.sh candidate.deb [previous.deb]"
fi

(( EUID != 0 )) || fail "Run as the desktop user with sudo available for apt."
[[ $(dpkg --print-architecture) == amd64 ]] || fail "An amd64 test host is required."
[[ -n ${DISPLAY:-} ]] || fail "Run in an X11 session or under xvfb-run -a."
python=/usr/bin/python3
"$python" -c 'import sqlite3, Xlib' || fail "Install python3 and python3-xlib on the test host."

script_dir=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)
package_name=muses
launcher=/usr/bin/muses
desktop_file=/usr/share/applications/muses.desktop

validate_package() {
    local package=$1
    [[ $package == *.deb && -f $package ]] || fail "Expected a .deb file: $package"
    [[ $(dpkg-deb -f "$package" Package) == "$package_name" ]] || fail "Unexpected package name: $package"
    [[ $(dpkg-deb -f "$package" Architecture) == amd64 ]] || fail "Expected an amd64 package: $package"
}

candidate=$(realpath -e -- "$1")
validate_package "$candidate"
candidate_version=$(dpkg-deb -f "$candidate" Version)
first=$candidate
first_version=$candidate_version
if (( $# == 2 )); then
    first=$(realpath -e -- "$2")
    validate_package "$first"
    first_version=$(dpkg-deb -f "$first" Version)
    dpkg --compare-versions "$first_version" lt "$candidate_version" || fail "The previous package must be older than the candidate."
fi

if dpkg-query -W "$package_name" >/dev/null 2>&1 || [[ -e $launcher || -e $desktop_file ]]; then
    fail "Use a clean test host without an existing Muses installation."
fi

sudo -v
work_dir=$(mktemp -d)
app_pid=
install_started=0
cleanup() {
    local result=$?
    if [[ -n $app_pid ]]; then
        kill -KILL "$app_pid" 2>/dev/null || true
        wait "$app_pid" 2>/dev/null || true
    fi
    if (( result != 0 )) && [[ -s $work_dir/app.log ]]; then
        cat "$work_dir/app.log" >&2
    fi
    if (( install_started )) && dpkg-query -W "$package_name" >/dev/null 2>&1; then
        sudo apt-get purge -y "$package_name" || result=1
    fi
    rm -rf -- "$work_dir"
    exit "$result"
}
trap cleanup EXIT
trap 'exit 130' INT
trap 'exit 143' TERM

# Keep the normal .NET/XDG path selection active inside an isolated home.
unset MUSES_DATA_HOME MUSES_CACHE_HOME
export HOME="$work_dir/home"
export XDG_CONFIG_HOME="$HOME/.config"
export XDG_DATA_HOME="$HOME/.local/share"
export XDG_CACHE_HOME="$HOME/.cache"
export XDG_RUNTIME_DIR="$work_dir/runtime"
mkdir -p -- "$XDG_CONFIG_HOME" "$XDG_DATA_HOME" "$XDG_CACHE_HOME"
mkdir -m 0700 -- "$XDG_RUNTIME_DIR"
preferred_data="$XDG_DATA_HOME/Muses"
fixture_data=$preferred_data
if (( $# == 2 )); then
    fixture_data="$XDG_CONFIG_HOME/Muses"
fi

fixture() {
    "$python" "$script_dir/package-smoke.py" "$1" "$fixture_data"
}

install_package() {
    local package=$1 expected_version=$2
    install_started=1
    sudo apt-get install -y "$package"
    [[ $(dpkg-query -W -f='${Status} ${Architecture} ${Version}' "$package_name") == "install ok installed amd64 $expected_version" ]] ||
        fail "Package registration does not match the candidate."
    [[ -x $launcher && -f $desktop_file ]] || fail "The installed launcher or desktop entry is missing."
}

launch_and_close() {
    "$launcher" >"$work_dir/app.log" 2>&1 &
    app_pid=$!
    "$python" "$script_dir/package-smoke.py" close-x11-window "$app_pid"
    timeout 15 tail --pid="$app_pid" -f /dev/null || fail "Muses did not close after the desktop exit request."
    local result=0
    wait "$app_pid" || result=$?
    app_pid=
    (( result == 0 )) || fail "Muses exited with code $result."
}

fixture prepare
install_package "$first" "$first_version"
launch_and_close

if (( $# == 2 )); then
    fixture seed-legacy
    install_package "$candidate" "$candidate_version"
    fixture verify-fixture
else
    fixture seed
fi
launch_and_close
fixture verify-restored
if (( $# == 2 )); then
    [[ ! -e $preferred_data/muses-youtube-native.sqlite ]] ||
        fail "The upgraded application opened a new database instead of the legacy one."
fi

sudo apt-get purge -y "$package_name"
if dpkg-query -W "$package_name" >/dev/null 2>&1 || [[ -e $launcher || -e $desktop_file ]]; then
    fail "Muses still appears to be installed after uninstall."
fi
fixture verify-restored
printf 'Muses %s installed, opened, restored the SQLite fixture and uninstalled.\n' "$candidate_version"
