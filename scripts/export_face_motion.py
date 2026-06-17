import json
import sys
from pathlib import Path

ROOT = Path('/mnt/d/github/sssekai')
UNITYPY_TEMP = Path('/mnt/d/github/sssekai_blender_io/.temp')
sys.path.insert(0, str(ROOT))
sys.path.append(str(UNITYPY_TEMP))

import UnityPy  # type: ignore
from UnityPy import load  # type: ignore
from UnityPy.enums import ClassIDType  # type: ignore
from sssekai.unity.AnimationClip import read_animation  # type: ignore

UnityPy.config.FALLBACK_UNITY_VERSION = '2022.3.21f1'
SEKAI_BLENDSHAPE_CRC = 2770785369


def export_face_motion(bundle_path: str, output_path: str):
    env = load(bundle_path)
    clips_out = []
    for obj in env.objects:
        if obj.type != ClassIDType.AnimationClip:
            continue
        data = obj.read()
        if data.m_Name not in ('face', 'face_loop'):
            continue
        anim = read_animation(data)
        curves = anim.CurvesT.get(SEKAI_BLENDSHAPE_CRC, {})
        clip = {
            'name': anim.Name,
            'sampleRate': anim.SampleRate,
            'curves': [],
        }
        max_time = 0.0
        for curve_hash, curve in sorted(curves.items()):
            keyframes = [
                {'time': float(kf.time), 'value': float(kf.value)}
                for kf in curve.Data
                if float(kf.time) > -1e20
            ]
            if keyframes:
                max_time = max(max_time, keyframes[-1]['time'])
            clip['curves'].append(
                {
                    'curveHash': int(curve_hash),
                    'keyframes': keyframes,
                }
            )
        clip['duration'] = max_time
        clips_out.append(clip)

    output = {
        'bundlePath': bundle_path,
        'clips': clips_out,
    }
    Path(output_path).write_text(json.dumps(output, indent=2), encoding='utf-8')


if __name__ == '__main__':
    if len(sys.argv) != 3:
        raise SystemExit('usage: export_face_motion.py <bundle> <output.json>')
    export_face_motion(sys.argv[1], sys.argv[2])
