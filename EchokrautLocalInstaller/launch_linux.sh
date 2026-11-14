#!/usr/bin/env bash
# Native half of the EchokrautLocalInstaller on Linux. The installer itself is a Windows exe running
# inside Wine (next to the game), so it cannot start, watch or kill a native process directly: Wine
# hands out no process handle for a Linux binary. It writes this script into the run folder, starts it
# through `start.exe /unix /bin/bash`, and from then on everything goes through files in that folder:
#   args       one argument per line: the wrapper's install_linux.sh, then its arguments
#   out.log    stdout + stderr of the bootstrap (the installer tails it)
#   pid        process (and process group) id of the bootstrap
#   heartbeat  touched by the installer while the game is alive; stale => shut the server down
#   stop       created by the installer to request a shutdown
#   exit       exit code of the bootstrap, written last
set -u
RUN="$1"
STALE_AFTER=30

mapfile -t ARGS < <(tr -d '\r' < "$RUN/args")
# The wrapper zip is built on Windows; a CRLF line ending in the shebang makes bash refuse the script.
sed -i 's/\r$//' "${ARGS[0]}"
cd "$(dirname "${ARGS[0]}")/.." || exit 1

# setsid makes the bootstrap the leader of its own process group, so one kill reaches uv, python and
# the server it spawns. Both writers append, so the launcher's own notes never overwrite its output.
setsid bash "${ARGS[@]}" >> "$RUN/out.log" 2>&1 < /dev/null &
CHILD=$!
echo "$CHILD" > "$RUN/pid"

note() { echo "[launch_linux] $*" >> "$RUN/out.log"; }

stop_child() {
    kill -TERM -- "-$CHILD" 2>/dev/null
    for _ in 1 2 3 4 5 6 7 8 9 10; do
        kill -0 "$CHILD" 2>/dev/null || return
        sleep 1
    done
    kill -KILL -- "-$CHILD" 2>/dev/null
}

while kill -0 "$CHILD" 2>/dev/null; do
    sleep 2
    if [ -e "$RUN/stop" ]; then
        note "stop requested by the installer"
        stop_child
        break
    fi
    beat=$(stat -c %Y "$RUN/heartbeat" 2>/dev/null || echo 0)
    if [ $(( $(date +%s) - beat )) -gt "$STALE_AFTER" ]; then
        note "installer heartbeat older than ${STALE_AFTER}s (game or installer gone); shutting down"
        stop_child
        break
    fi
done

wait "$CHILD"
echo $? > "$RUN/exit"
