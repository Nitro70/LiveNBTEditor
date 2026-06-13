"""LiveNBT smoke test. Usage:
    python tools/smoke.py <token> [host] [port] [player-name]
Connects, authenticates, lists roots, dumps player top-level keys,
toggles abilities.mayfly, then watches Pos for 5 seconds."""
import asyncio
import json
import sys

import websockets

TOKEN = sys.argv[1]
HOST = sys.argv[2] if len(sys.argv) > 2 else "127.0.0.1"
PORT = int(sys.argv[3]) if len(sys.argv) > 3 else 25599
NEXT_ID = 0


async def request(ws, op, **kwargs):
    global NEXT_ID
    NEXT_ID += 1
    await ws.send(json.dumps({"id": NEXT_ID, "op": op, **kwargs}))

    async def await_reply():
        while True:
            msg = json.loads(await ws.recv())
            if msg.get("id") == NEXT_ID:
                if not msg.get("ok"):
                    raise RuntimeError(f"{op} failed: {msg.get('error')}")
                return msg
            print("  (push)", json.dumps(msg)[:120])

    try:
        return await asyncio.wait_for(await_reply(), 15)  # asyncio.timeout needs 3.11+
    except asyncio.TimeoutError:
        raise RuntimeError(
            f"{op}: no reply in 15s — if singleplayer, the game pauses when unfocused; "
            "press F3+P in-game or set pauseOnLostFocus:false in options.txt") from None


async def main():
    async with websockets.connect(f"ws://{HOST}:{PORT}/") as ws:
        hello = json.loads(await ws.recv())
        assert hello["op"] == "hello", hello
        print("hello:", hello)

        await request(ws, "auth", token=TOKEN)
        print("auth: ok")

        roots = (await request(ws, "roots"))["value"]
        print("roots:", roots)
        player = sys.argv[4] if len(sys.argv) > 4 else roots["players"][0]
        root = f"player:{player}"

        snap = (await request(ws, "get", root=root, path=""))["value"]
        print(f"player keys: {sorted(snap['v'].keys())}")

        mayfly = (await request(ws, "get", root=root, path="abilities.mayfly"))["value"]
        new = {"t": "byte", "v": 0 if mayfly["v"] else 1}
        await request(ws, "set", root=root, path="abilities.mayfly", value=new)
        print(f"abilities.mayfly: {mayfly['v']} -> {new['v']} (check in-game: double-tap space)")

        await request(ws, "watch", root=root, path="Pos")
        print("watching Pos for 5s — walk around in-game:")

        async def watch_pos():
            while True:
                msg = json.loads(await ws.recv())
                if msg.get("op") == "update" and msg.get("value"):
                    print("  Pos =", [round(float(n["v"]), 2) for n in msg["value"]["v"]])

        try:
            await asyncio.wait_for(watch_pos(), 5)
        except asyncio.TimeoutError:
            pass

        # error-path check: invalid type for Health
        try:
            await request(ws, "set", root=root, path="Health", value={"t": "string", "v": "abc"})
            print("ERROR: bad-type set was accepted!")
        except RuntimeError as e:
            print("bad-type set correctly rejected:", e)

        print("SMOKE TEST PASSED")


asyncio.run(main())
