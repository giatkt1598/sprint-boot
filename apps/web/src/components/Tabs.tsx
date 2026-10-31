import MuiTab from '@mui/material/Tab';
import MuiTabs from '@mui/material/Tabs';
import { font, sem } from '../theme/tokens';

export interface TabItem {
  value: string;
  label: string;
}

export interface TabsProps {
  label: string;
  items: TabItem[];
  value: string;
  onChange: (value: string) => void;
}

export function Tabs({ label, items, value, onChange }: TabsProps) {
  return (
    <MuiTabs
      aria-label={label}
      value={value}
      onChange={(_, next: string) => onChange(next)}
      sx={{ '& .MuiTabs-indicator': { backgroundColor: sem('text-accent'), height: 2 } }}
    >
      {items.map((item) => (
        <MuiTab
          key={item.value}
          value={item.value}
          label={item.label}
          sx={{
            fontSize: font.size.md,
            fontWeight: font.weight.medium,
            color: sem('text-secondary'),
            '&.Mui-selected': { color: sem('text-accent') },
          }}
        />
      ))}
    </MuiTabs>
  );
}
