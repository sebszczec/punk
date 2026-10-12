#!/bin/bash
# Drives the Linux build inside tmux with real keystrokes against a fake Steam tree.
# Run from Windows:  wsl -d Ubuntu-22.04 -e bash /mnt/c/Users/sebsz/source/punk/scripts/linux_test.sh
# Needs: tmux in the distro, and dist/linux-x64/Punk published (./publish.ps1).
BIN=${PUNK_BIN:-/mnt/c/Users/sebsz/source/punk/dist/linux-x64/Punk}
H=$(mktemp -d)
S=$H/.steam/steam/steamapps
mkdir -p "$S"
mk() { printf '"AppState"\n{\n\t"appid"\t"%s"\n\t"name"\t"%s"\n\t"StateFlags"\t"4"\n\t"installdir"\t"x"\n\t"SizeOnDisk"\t"%s"\n\t"LastPlayed"\t"%s"\n}\n' "$1" "$2" "$3" "$4" > "$S/appmanifest_$1.acf"; }
mk 1 "Half-Life 3" 5000000000 $(date +%s)
mk 2 "Portal Reloaded" 900000000 1700000000
mk 3 "Obscure Game" 120000000 1600000000
printf '"libraryfolders"\n{\n\t"0"\n\t{\n\t\t"path"\t"%s/.steam/steam"\n\t}\n}\n' "$H" > "$S/libraryfolders.vdf"

cap() { echo "=== $1 ==="; tmux capture-pane -t t -p | grep -v '^ *$' | grep -v '^[│╭╰] \{60\}'; }

tmux kill-session -t t 2>/dev/null
tmux -f /dev/null new-session -d -s t -x 120 -y 30 "env HOME=$H TERM=xterm-256color $BIN 2>$H/err.txt"
sleep 1.5; cap splash
sleep 3;   cap main
tmux send-keys -t t o b; sleep 0.8; cap "filter ob"
tmux send-keys -t t Escape; sleep 0.8
tmux send-keys -t t Down; sleep 0.5; cap "down"
tmux send-keys -t t F2; sleep 1.2; cap settings
tmux send-keys -t t Escape; sleep 0.8; cap "after esc"
tmux send-keys -t t C-q; sleep 1
if tmux has-session -t t 2>/dev/null; then echo "STILL RUNNING after Ctrl+Q"; tmux kill-session -t t; else echo "exited cleanly"; fi
echo "--- stderr ---"; head -c 1000 "$H/err.txt"
