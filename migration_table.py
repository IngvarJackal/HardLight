import csv

def load_data(filepath):
    data = {}
    try:
        with open(filepath, 'r', encoding='utf-8') as f:
            reader = csv.reader(f, delimiter='\t')
            for row in reader:
                if len(row) >= 5:
                    # Clean the fields
                    clean_row = [field.strip() for field in row]
                    data[clean_row[0]] = {
                        'id': clean_row[0],
                        'file': clean_row[1],
                        'discipline': clean_row[2],
                        'tier': clean_row[3],
                        'position': clean_row[4],
                        'prereqs': clean_row[5] if len(clean_row) > 5 else ''
                    }
    except FileNotFoundError:
        print(f"Error: {filepath} not found.")
    return data

def main():
    vrs_data = load_data('/tmp/vrs_tech.tsv')
    triad_data = load_data('/tmp/triad_tech.tsv')
    
    diff_ids = []
    try:
        with open('/tmp/shared_meta_diffs.tsv', 'r', encoding='utf-8') as f:
            for line in f:
                parts = line.split('\t')
                if parts:
                    diff_ids.append(parts[0].strip())
    except Exception as e:
        print(f"Error loading diff IDs: {e}")
        return

    print(f"{'ID':<30} | {'Src':<5} | {'Discipline':<20} | {'T':<2} | {'Pos':<10} | {'Prereqs'}")
    print("-" * 120)

    missing_pos_vrs = []

    for id in diff_ids:
        # Check all tech for missing position, not just diffs
        pass

    for id, tech in vrs_data.items():
        if not tech.get('position'):
            missing_pos_vrs.append(id)

    for id in diff_ids:
        vrs = vrs_data.get(id, {})
        triad = triad_data.get(id, {})
            
        print(f"{id:<30} | {'VRS':<5} | {vrs.get('discipline',''):<20} | {vrs.get('tier',''):<2} | {vrs.get('position',''):<10} | {vrs.get('prereqs','')}")
        print(f"{'':<30} | {'Triad':<5} | {triad.get('discipline',''):<20} | {triad.get('tier',''):<2} | {triad.get('position',''):<10} | {triad.get('prereqs','')}")
        print(f"  VRS:   {vrs.get('file','')}")
        print(f"  Triad: {triad.get('file','')}")
        print("-" * 120)

    if missing_pos_vrs:
        print("\nIDs with missing/blank position in VRS (Total: {}):".format(len(missing_pos_vrs)))
        for id in sorted(missing_pos_vrs):
            print(f" - {id}")
    else:
        print("\nNo IDs with missing position found in VRS.")

if __name__ == "__main__":
    main()
