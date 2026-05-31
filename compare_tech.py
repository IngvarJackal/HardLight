import yaml
import glob
import os

def load_tech_costs(path_pattern):
    techs = {}
    for filepath in glob.glob(path_pattern, recursive=True):
        try:
            with open(filepath, 'r') as f:
                content = yaml.safe_load(f)
                if not content: continue
                for entry in content:
                    if entry.get('type') == 'technology':
                        tid = entry.get('id')
                        cost = entry.get('cost')
                        if tid:
                            techs[tid] = {'cost': cost, 'file': filepath}
        except Exception as e:
            print(f"Error reading {filepath}: {e}")
    return techs

vrs_path = "/home/ubuntu/Github/VRS-xeno-port/Resources/Prototypes/**/Research/*.yml"
triad_path = "/home/ubuntu/Github/Triad_Sector/Resources/Prototypes/**/Research/*.yml"

vrs_techs = load_tech_costs(vrs_path)
triad_techs = load_tech_costs(triad_path)

vrs_ids = set(vrs_techs.keys())
triad_ids = set(triad_techs.keys())
shared_ids = vrs_ids.intersection(triad_ids)

diffs = []
for tid in shared_ids:
    if vrs_techs[tid]['cost'] != triad_techs[tid]['cost']:
        diffs.append((tid, vrs_techs[tid]['cost'], triad_techs[tid]['cost'], vrs_techs[tid]['file']))

print("--- DIFFERING COSTS ---")
for d in sorted(diffs):
    print(f"{d[0]} | {d[1]} | {d[2]} | {d[3]}")

print(f"\nDiff count: {len(diffs)}")
print(f"Shared IDs count: {len(shared_ids)}")
print(f"Only in Triad: {len(triad_ids - vrs_ids)}")
print(f"Only in VRS: {len(vrs_ids - triad_ids)}")
