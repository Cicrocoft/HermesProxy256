#!/usr/bin/env bash
# Launch HermesProxy for a 2.5.6 (build 69110) client against a 2.4.3 emulator.
#
#   1. Fill in the three values below.
#   2. bash scripts/run-2.5.6.sh
#
# That is all. Everything under "Feature flags" is already set to the combination this build needs
# and should not need touching.
set -u

# ---------------------------------------------------------------------------
# YOUR SETTINGS - the only lines you need to edit
# ---------------------------------------------------------------------------
# Use an account that exists ONLY on the emulator you are proxying to, and a password you use
# nowhere else. The 2.5.6 client authenticates with SRP and never sends its password to the proxy,
# while the legacy TBC logon server still requires it in the clear - so the proxy has to hold it in
# a form it can replay, and no way of storing it protects it from anything with access to this
# machine. Environment variables already set win over these, so you can also export them instead of
# editing this file.
: "${HERMES_AccountOptions__Username:=YOURACCOUNT}"
: "${HERMES_AccountOptions__Password:=yourpassword}"
: "${HERMES_LegacyServerOptions__Address:=127.0.0.1}"
: "${HERMES_LegacyServerOptions__Port:=3724}"

if [ "$HERMES_AccountOptions__Username" = "YOURACCOUNT" ]; then
    echo "run-2.5.6: edit the account settings at the top of this file first." >&2
    exit 1
fi
export HERMES_AccountOptions__Username HERMES_AccountOptions__Password
export HERMES_LegacyServerOptions__Address HERMES_LegacyServerOptions__Port

# ---------------------------------------------------------------------------
# Fixed configuration for this build
# ---------------------------------------------------------------------------
# appsettings.json ships with ClientBuild=V2_5_2_40892 and no AccountOptions, and Program.cs reads
# environment variables with the "HERMES_" prefix only - a plain ClientOptions__ClientBuild is
# ignored. Without these the proxy starts as a 2.5.2 proxy against 127.0.0.1, the client connects,
# sees the wrong build and disconnects with ErrorCode=0, which at the login screen looks exactly
# like a rejected password.
export HERMES_ClientOptions__ClientBuild="V2_5_6_69110"
export HERMES_AccountOptions__SrpVersion="2"

# The 5.5.0-engine client validates the login endpoint's certificate and abandons the TLS handshake
# against the embedded self-signed one, so no HTTP request ever reaches the router. Serving the
# login REST endpoint in the clear is what makes it connect at all; SRP protects the exchange.
export HERMES_ProxyNetworkOptions__RestPlaintext="true"

export HERMES_LoggingOptions__MinimumLevel="Debug"
export HERMES_LoggingOptions__ServerLevel="Debug"
export HERMES_LoggingOptions__NetworkLevel="Debug"
export HERMES_LoggingOptions__PacketLevel="Debug"

# ---------------------------------------------------------------------------
# Feature flags
# ---------------------------------------------------------------------------
# 2.5.6 support is built as opt-in switches so each one can be turned off on its own. The defaults
# below are the combination confirmed working; with them off the game looks broken in ways that are
# easy to misread as new bugs. Override any of them from the environment to bisect.
export HERMES_256_VEJB="${HERMES_256_VEJB:-1}"
export HERMES_256_QUESTLOG="${HERMES_256_QUESTLOG:-1}"
export HERMES_256_INVSLOTS="${HERMES_256_INVSLOTS:-1}"
export HERMES_256_ITEMBONUSKEY="${HERMES_256_ITEMBONUSKEY:-1}"
export HERMES_256_NAMESRESPONSE="${HERMES_256_NAMESRESPONSE:-1}"
export HERMES_256_HOTFIX553="${HERMES_256_HOTFIX553:-1}"
export HERMES_256_CREATEBITS="${HERMES_256_CREATEBITS:-1}"
export HERMES_256_ENVELOPEBIT="${HERMES_256_ENVELOPEBIT:-1}"
export HERMES_256_LOGININIT="${HERMES_256_LOGININIT:-1}"
export HERMES_256_CREATEORDER="${HERMES_256_CREATEORDER:-1}"
export HERMES_256_NOCUSTOM="${HERMES_256_NOCUSTOM:-1}"
export HERMES_256_UNITTRAILER1="${HERMES_256_UNITTRAILER1:-1}"
export HERMES_256_VALUESUPDATE="${HERMES_256_VALUESUPDATE:-4}"
export HERMES_256_VALFIRST="${HERMES_256_VALFIRST:-1}"
export HERMES_256_ITEMCREATE1="${HERMES_256_ITEMCREATE1:-1}"
export HERMES_256_APDINV116="${HERMES_256_APDINV116:-1}"
export HERMES_256_PDSHIFT1="${HERMES_256_PDSHIFT1:-1}"
export HERMES_256_UNITARR1="${HERMES_256_UNITARR1:-1}"
export HERMES_256_PCFLAG="${HERMES_256_PCFLAG:-1}"
export HERMES_256_KNOWNSPELLSLOGIN="${HERMES_256_KNOWNSPELLSLOGIN:-1}"
export HERMES_256_SPELLSTART="${HERMES_256_SPELLSTART:-1}"
export HERMES_256_TRAINEROPCODE="${HERMES_256_TRAINEROPCODE:-1}"
export HERMES_256_TRAINER553="${HERMES_256_TRAINER553:-1}"
export HERMES_256_LEARNEDSPELLS3="${HERMES_256_LEARNEDSPELLS3:-1}"
export HERMES_256_NPCGOSSIPBIT="${HERMES_256_NPCGOSSIPBIT:-1}"
export HERMES_256_BUYBACKVALUES="${HERMES_256_BUYBACKVALUES:-1}"
export HERMES_256_INVSLOTMAP="${HERMES_256_INVSLOTMAP:-1}"
export HERMES_256_BUYITEM553="${HERMES_256_BUYITEM553:-1}"
export HERMES_256_CHARCREATE553="${HERMES_256_CHARCREATE553:-1}"
export HERMES_256_QUESTCOMPLETED="${HERMES_256_QUESTCOMPLETED:-1}"
export HERMES_256_APDPROBE="${HERMES_256_APDPROBE:-0}"
export HERMES_256_NPCFLAGS="${HERMES_256_NPCFLAGS:-1}"
export HERMES_256_ITEMLOOK="${HERMES_256_ITEMLOOK:-1}"
export HERMES_256_UNITFIELDS="${HERMES_256_UNITFIELDS:-1}"
export HERMES_256_ACTIVETAG="${HERMES_256_ACTIVETAG:-0}"
export HERMES_256_WATCHEDFACTION="${HERMES_256_WATCHEDFACTION:-1}"
export HERMES_256_CAMERATABLE="${HERMES_256_CAMERATABLE:-1}"
export HERMES_256_UNITTRIM="${HERMES_256_UNITTRIM:-0}"
export HERMES_256_ITEMZERO="${HERMES_256_ITEMZERO:-0}"
export HERMES_256_ZERO="${HERMES_256_ZERO:-}"
export HERMES_256_ITEMOWNER="${HERMES_256_ITEMOWNER:-1}"
export HERMES_256_UNITTAIL="${HERMES_256_UNITTAIL:-1}"
export HERMES_256_UNITDROPVI="${HERMES_256_UNITDROPVI:-1}"
export HERMES_256_APDDROPARR="${HERMES_256_APDDROPARR:-0}"
export HERMES_256_ITEMSFIRST="${HERMES_256_ITEMSFIRST:-1}"
export HERMES_256_TABARD10="${HERMES_256_TABARD10:-0}"
export HERMES_256_CREATUREQUERY="${HERMES_256_CREATUREQUERY:-1}"
export HERMES_256_CREATURENAMELEN="${HERMES_256_CREATURENAMELEN:-1}"
export HERMES_256_APDPAD="${HERMES_256_APDPAD:-128}"
export HERMES_256_PLAYERNAME="${HERMES_256_PLAYERNAME:-0}"
export HERMES_256_SETUPLAST="${HERMES_256_SETUPLAST:-0}"
export HERMES_256_EXPANSIONENUM="${HERMES_256_EXPANSIONENUM:-1}"
export HERMES_256_ENUMFLAGS="${HERMES_256_ENUMFLAGS:-0}"
export HERMES_256_QUESTIDMAP="${HERMES_256_QUESTIDMAP:-1}"
export HERMES_256_QUESTMAPVALUES="${HERMES_256_QUESTMAPVALUES:-1}"
export HERMES_256_QUESTKEEPPLAYER="${HERMES_256_QUESTKEEPPLAYER:-1}"
export HERMES_256_QUESTLOGFULL="${HERMES_256_QUESTLOGFULL:-1}"
export HERMES_256_TURNINCLOSE="${HERMES_256_TURNINCLOSE:-1}"
export HERMES_256_QCRECREATE="${HERMES_256_QCRECREATE:-1}"
export HERMES_256_CONTAINERONLY="${HERMES_256_CONTAINERONLY:-1}"
export HERMES_256_CORPSEOWNER="${HERMES_256_CORPSEOWNER:-1}"
export HERMES_256_POWERORDER="${HERMES_256_POWERORDER:-1}"
export HERMES_256_NOLIGHTNING="${HERMES_256_NOLIGHTNING:-1}"
export HERMES_256_DEATHLOC="${HERMES_256_DEATHLOC:-1}"
export HERMES_256_SPIRITHEALER="${HERMES_256_SPIRITHEALER:-1}"
export HERMES_256_CORPSELOC="${HERMES_256_CORPSELOC:-1}"
export HERMES_256_ACTIONBUTTONS="${HERMES_256_ACTIONBUTTONS:-1}"
export HERMES_256_EXPLOREDZONES="${HERMES_256_EXPLOREDZONES:-1}"
export HERMES_256_EXPLORERECREATE="${HERMES_256_EXPLORERECREATE:-0}"
export HERMES_256_LOOTREASON="${HERMES_256_LOOTREASON:-1}"

# ---------------------------------------------------------------------------
# Launch. Nothing may be added below: exec replaces the shell.
# ---------------------------------------------------------------------------
echo "[run-2.5.6] live flags:"
env | grep "^HERMES_256_" | sort | sed "s/^/    /"

DOTNET="${DOTNET:-dotnet}"
cd "$(dirname "$0")/../HermesProxy/bin/Debug" || exit 1
exec "$DOTNET" HermesProxy.dll
