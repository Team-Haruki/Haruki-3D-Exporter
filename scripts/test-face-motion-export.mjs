import assert from 'node:assert/strict';
import { mkdtemp, readFile, writeFile } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { spawnSync } from 'node:child_process';
import { test } from 'node:test';

const repoRoot = new URL('..', import.meta.url).pathname;
const dotnet = process.env.DOTNET_BIN ?? 'dotnet';
const assetStudioRoot = process.env.ASSETSTUDIO_ROOT;

test('export-face-motion converts decoded AnimationClip JSON', async () => {
  const dir = await mkdtemp(join(tmpdir(), 'haruki-face-motion-'));
  const inputPath = join(dir, 'face.json');
  const outputPath = join(dir, 'face_motion.json');
  const decodedClip = {
    name: 'face',
    sampleRate: 60,
    duration: 0.5,
    curves: [
      {
        binding: {
          path: 2770785369,
          attribute: 12345,
          typeId: 'SkinnedMeshRenderer',
        },
        keys: [
          {
            time: 0,
            values: [0],
            inSlopes: [0],
            outSlopes: [2],
            isDense: false,
            isConstant: false,
          },
          {
            time: 0.5,
            values: [1],
            inSlopes: [2],
            outSlopes: [0],
            isDense: false,
            isConstant: false,
          },
        ],
      },
      {
        binding: {
          path: 1,
          attribute: 999,
          typeId: 'SkinnedMeshRenderer',
        },
        keys: [
          {
            time: 0,
            values: [1],
            inSlopes: [0],
            outSlopes: [0],
            isDense: false,
            isConstant: false,
          },
        ],
      },
    ],
  };
  await writeFile(inputPath, JSON.stringify(decodedClip), 'utf8');

  const args = [
    'run',
    '--no-build',
    '--project',
    repoRoot,
  ];
  if (assetStudioRoot) {
    args.push('-p:AssetStudioRoot=' + assetStudioRoot);
  }
  args.push(
    '--',
    '--export-face-motion',
    '--motion',
    inputPath,
    '--out',
    outputPath,
    '--source-path',
    'character/motion/costume_setting/01_00.bundle',
  );

  const result = spawnSync(
    dotnet,
    args,
    {
      cwd: repoRoot,
      encoding: 'utf8',
    },
  );

  assert.equal(result.status, 0, result.stderr || result.stdout);
  const output = JSON.parse(await readFile(outputPath, 'utf8'));
  assert.equal(output.bundlePath, 'character/motion/costume_setting/01_00.bundle');
  assert.equal(output.clips.length, 1);
  assert.equal(output.clips[0].name, 'face');
  assert.equal(output.clips[0].sampleRate, 120);
  assert.equal(output.clips[0].duration, 0.5);
  assert.equal(output.clips[0].curves.length, 1);
  assert.equal(output.clips[0].curves[0].curveHash, 12345);
  assert.equal(output.clips[0].curves[0].keyframes.length, 61);
  assert.deepEqual(output.clips[0].curves[0].keyframes[0], { time: 0, value: 0 });
  assert.deepEqual(output.clips[0].curves[0].keyframes.at(-1), { time: 0.5, value: 1 });
});
