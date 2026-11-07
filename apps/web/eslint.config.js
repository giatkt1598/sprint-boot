import js from '@eslint/js';
import tseslint from 'typescript-eslint';

export default tseslint.config(
  { ignores: ['dist', 'test-results', 'playwright-report'] },
  js.configs.recommended,
  ...tseslint.configs.recommended,
  {
    // Icons go through the themed registry, so a theme can bring its own set. Only the registry and the theme sets
    // import an icon library.
    files: ['src/**/*.{ts,tsx}'],
    ignores: ['src/components/icons.tsx', 'src/theme/icons/**'],
    rules: {
      'no-restricted-imports': [
        'error',
        {
          paths: [
            { name: 'lucide-react', message: 'Import themed icons from components/icons instead.' },
            { name: '@phosphor-icons/react', message: 'Import themed icons from components/icons instead.' },
            { name: '@tabler/icons-react', message: 'Import themed icons from components/icons instead.' },
            { name: 'iconoir-react', message: 'Import themed icons from components/icons instead.' },
          ],
          patterns: [{ group: ['@heroicons/react/*'], message: 'Import themed icons from components/icons instead.' }],
        },
      ],
    },
  },
);
