"""Apply the same small compatibility fixes as VRCLearn's existing VPM website."""
from pathlib import Path
import sys


def replace_exact(path: Path, old: str, new: str, count: int):
    text = path.read_text(encoding='utf-8')
    if text.count(old) != count:
        raise ValueError(f'Pinned official template changed: {path.name}')
    path.write_text(text.replace(old, new), encoding='utf-8')


if __name__ == '__main__':
    root = Path(sys.argv[1])
    replace_exact(root / 'index.html', '>Add to VCC<', '>Add to VCC/ALCOMD<', 2)
    replace_exact(root / 'index.html', 'grid-template-columns="1fr 100px 220px"',
                  'grid-template-columns="1fr 100px 280px"', 1)
    replace_exact(root / 'app.js', '{{ if package.Description; package.Description; end; }}',
                  '{{ if package.Description; package.Description | string.escape; end; }}', 1)
