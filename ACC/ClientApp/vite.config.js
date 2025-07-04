import { defineConfig } from 'vite';
import { viteStaticCopy } from 'vite-plugin-static-copy';

export default defineConfig({
    base: '/dist/',
    build: {
        target: 'esnext',
        outDir: '../wwwroot/dist',
        emptyOutDir: true,
        assetsDir: 'assets',
         minify: false,
        rollupOptions: {
            input: 'src/index.js',
            output: {
                manualChunks: undefined,
                entryFileNames: 'assets/viewer.js', 
                chunkFileNames: 'assets/[name].js',
                assetFileNames: 'assets/[name].[ext]'
    },
        },
        chunkSizeWarningLimit: 2000,
     
    },
    assetsInclude: ['**/*.wasm'],
    publicDir: 'public',
    server: {
        cors: true
    },
    plugins: [
        viteStaticCopy({
            targets: [
                {
                    src: 'node_modules/web-ifc/web-ifc.wasm',
                    dest: 'assets/lib/web-ifc'
                }
                ,
                {
                    src: 'node_modules/web-ifc/web-ifc-mt.wasm',
                    dest: 'assets/lib/web-ifc'
                }
            ]
        })
    ],
    optimizeDeps: {
        include: [
            '@thatopen/components',
            '@thatopen/components-front',
            '@thatopen/ui',
            '@thatopen/fragments',
            'three'
        ],
        exclude: []
    },
    resolve: {
        alias: {
            'three': 'three'
        }
    }
});