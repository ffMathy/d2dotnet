#!/bin/sh
# Fake d2-map.exe speaking the same stdin/stdout JSON-lines protocol as upstream map/main.c and the C# generator.
# Run as: /bin/sh fake-d2-map.sh <game path>
# Only shell builtins are used because the generator is started with an empty environment (no PATH).
# Behaviour switches are marker files inside the game path directory:
#   no-init      never print the init message
#   bad-echo     echo a different seed than requested
#   crash-once   exit half way through the first $map (marker "crashed" is then created)
#   big-error    print "We got a big Error here" on stderr instead of generating
# Every start is recorded by appending a line to "<game path>/starts".
game="$1"
echo "start" >> "$game/starts"

echo '{"level":30,"time":1636000000000,"source":"main.c:93","msg":"Cli:Start","version":"fake","hash":"fake"}'
echo 'wine: not json'
echo '{"level":20,"time":1636000000001,"source":"main.c:150","msg":"Map:Init:Done"}'
if [ -f "$game/no-init" ]; then
    while IFS= read -r line; do :; done
    exit 0
fi
echo '{"type":"init"}'

seed=4278255360
difficulty=0
act=-1

info() {
    if [ "$act" -gt -1 ]; then
        echo "{\"type\":\"info\",\"seed\":$1,\"difficulty\":$difficulty,\"act\":$act}"
    else
        echo "{\"type\":\"info\",\"seed\":$1,\"difficulty\":$difficulty}"
    fi
}

level1() {
    echo "{\"type\":\"map\",\"id\":1,\"name\":\"Rogue Camp $seed\",\"offset\":{\"x\":100,\"y\":200},\"size\":{\"width\":40,\"height\":30},\"objects\":[{\"id\":119,\"type\":\"object\",\"x\":20,\"y\":20,\"name\":\"waypoint\",\"op\":23,\"class\":\"waypoint\"},{\"id\":2,\"type\":\"exit\",\"x\":35,\"y\":10,\"isGoodExit\":true},{\"id\":0,\"type\":\"npc\",\"x\":3,\"y\":3},{\"id\":5,\"type\":\"npc\",\"x\":4,\"y\":4},{\"id\":999,\"type\":\"npc\",\"x\":5,\"y\":5},{\"id\":580,\"type\":\"object\",\"x\":6,\"y\":6,\"class\":\"chest-super\"}],\"map\":[[1,5,1],[2,3,2],[1,5,1],[]]}"
}
level2() {
    echo "{\"type\":\"map\",\"id\":2,\"name\":\"Blood Moor\",\"offset\":{\"x\":140,\"y\":200},\"size\":{\"width\":10,\"height\":10},\"objects\":[],\"map\":[[10]]}"
}
level40() {
    echo "{\"type\":\"map\",\"id\":40,\"name\":\"Lut Gholein\",\"offset\":{\"x\":5000,\"y\":5000},\"size\":{\"width\":20,\"height\":20},\"objects\":[{\"id\":1,\"type\":\"exit\",\"x\":0,\"y\":0}],\"map\":[[20]]}"
}

dump_maps() {
    if [ "$act" -eq -1 ] || [ "$act" -eq 0 ]; then
        level1
        if [ -f "$game/crash-once" ] && [ ! -f "$game/crashed" ]; then
            echo "crashed" > "$game/crashed"
            exit 3
        fi
        echo '{"level":20,"time":1636000000002,"source":"main.c:70","msg":"Map:Generation","mapId":1}'
        level2
    fi
    if [ "$act" -eq -1 ] || [ "$act" -eq 1 ]; then
        level40
    fi
    echo "{\"level\":30,\"time\":1636000000003,\"source\":\"main.c:80\",\"msg\":\"Map:Generation:Done\",\"seed\": \"$seed\"}"
    echo ""
}

while IFS= read -r line; do
    set -- $line
    case "$1" in
        '$exit') exit 0 ;;
        '$map')
            if [ -f "$game/big-error" ]; then
                echo "We got a big Error here" >&2
            else
                dump_maps
                act=-1
                echo '{"type":"done"}'
            fi
            ;;
        '$seed')
            seed=${2%%.*} # scanf("%u") stops at the decimal point
            if [ -f "$game/bad-echo" ]; then info 1; else info "$seed"; fi
            ;;
        '$difficulty')
            difficulty=$2
            info "$seed"
            ;;
        '$act')
            act=$2
            info "$seed"
            ;;
    esac
    echo ""
done
