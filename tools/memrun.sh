#!/bin/bash
# 多次运行 --memprobe，输出私有内存中位数（单次测量波动可达 ±20MB）
EXE="./src/QuickBoard/bin/Release/net8.0-windows/QuickBoard.exe"
N=${2:-3}
vals=()
for i in $(seq 1 $N); do
  $EXE --memprobe "$PWD/tools/_m.txt" >/dev/null 2>&1
  v=$(grep -o "Private: [0-9.]*" tools/_m.txt | head -1 | awk '{print $2}')
  vals+=("$v")
done
printf "%s 次数=%s 各次: %s\n" "$1" "$N" "${vals[*]}"
printf "%s\n" "${vals[@]}" | sort -n | awk -v tag="$1" '{a[NR]=$1} END {printf "%s 中位数=%.1f MB\n", tag, a[int((NR+1)/2)]}'
